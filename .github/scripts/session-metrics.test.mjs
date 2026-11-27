// Tests for session-metrics.mjs (decision #108): `node --test .github/scripts/session-metrics.test.mjs`.
import { test } from "node:test";
import assert from "node:assert/strict";
import { addEvents, decodeState, emptyState, encodeState, prune, render, syncSessionMetrics } from "./session-metrics.mjs";

const summary = (over = {}) => ({
  v: 1, mod: "1.20.14", game: "0.9", ended: "menu", minutes: 30, players: 1, characters: "Ironclad", cores: 8, otherMods: false,
  combats: 3, turns: 20, clean: 16, unchecked: 2, capped: 1, mismatched: 1,
  forecast: { checked: 15, matched: 14, skipped: 2, skipReasons: { "an extra turn (no enemy turn to compare)": 2 }, mismatches: { "HP lost": 1 } },
  plays: { checked: 40, matched: 40, skipped: 1, skipReasons: { "simulation pending": 1 }, mismatches: {} },
  searches: { count: 300, background: 3, cancelled: 20, copies: 9000, cancelledCopies: 500, maxHeld: 1200, slowest: "Bash (target 0)",
    ms: { count: 280, p50: 0.4, p95: 3, p99: 9, max: 140 }, caps: { "the memory cap": 1 } },
  waits: { missed: 4, ms: { count: 100, p50: 0.3, p95: 6, p99: 8, max: 8 } },
  unsettled: { count: 4, p50: 80, p95: 140, p99: 140, max: 140 },
  frames: { slow: 2, gameStalls: 10, modMs: { count: 54000, p50: 0.8, p95: 2.1, p99: 5, max: 41 } },
  gc: { gen0: 50, gen1: 10, gen2: 1, pauseMs: 30, allocatedMb: 900 },
  reports: { proc: 1 },
  ...over,
});
const event = (id, date, s, tags = []) => ({ eventID: id.padEnd(32, "0"), dateCreated: date, tags, context: { summary: JSON.stringify(s) } });

test("adds summaries per version, once each", () => {
  const st = emptyState();
  const page = [event("a", "2026-11-19T10:00:00Z", summary()), event("b", "2026-11-19T11:00:00Z", summary({ ended: "unseen", players: 2 }))];
  assert.equal(addEvents(st, page), 2);
  assert.equal(addEvents(st, page), 0, "the overlap with the last run isn't counted twice");
  const t = st.versions["1.20.14"];
  assert.equal(t.sessions, 2);
  assert.equal(t.turns, 40);
  assert.equal(t.clean, 32);
  assert.equal(t.multiplayer, 1);
  assert.deepEqual(t.ended, { menu: 1, unseen: 1 });
  assert.equal(t.forecast.skipReasons["an extra turn (no enemy turn to compare)"], 4);
  assert.equal(t.searches.caps["the memory cap"], 2);
  assert.equal(t.frames.ms.p95Sum, 4.2);
  assert.equal(st.cursor, "2026-11-19T11:00:00Z");
});

test("adds the audits outside the turns by name, and keeps older totals that had none", () => {
  const st = emptyState();
  const others = { intent: { checked: 10, matched: 9, skipped: 1, skipReasons: { "the enemy's move rules couldn't be read": 1 }, mismatches: { JawWorm: 1 } } };
  addEvents(st, [event("c", "2026-11-20T10:00:00Z", summary({ mod: "1.21.10", others })), event("d", "2026-11-20T11:00:00Z", summary({ mod: "1.21.10", others }))]);
  const t = st.versions["1.21.10"];
  assert.deepEqual([t.others.intent.checked, t.others.intent.matched, t.others.intent.skipped], [20, 18, 2]);
  assert.equal(t.others.intent.mismatches.JawWorm, 2);
  assert.ok(render(prune(st)).includes("intent 18 / 20 (2)"));
  const old = emptyState();
  addEvents(old, [event("e", "2026-11-20T10:00:00Z", summary())]);
  delete old.versions["1.20.14"].others; // kept by a sync from before the field
  addEvents(old, [event("f", "2026-11-20T12:00:00Z", summary({ others }))]);
  assert.equal(old.versions["1.20.14"].others.intent.checked, 10);
});

test("skips the developer's own sessions and summaries it can't read", () => {
  const st = emptyState();
  const n = addEvents(st, [
    event("a", "2026-11-19T10:00:00Z", summary(), [{ key: "dev", value: "true" }]),
    event("b", "2026-11-19T10:00:00Z", summary({ v: 2 })),
    { eventID: "c".repeat(32), dateCreated: "2026-11-19T10:00:00Z", tags: [], context: { summary: "{broken" } },
  ]);
  assert.equal(n, 0);
  assert.deepEqual(st.versions, {});
});

test("the state survives the issue body", () => {
  const st = emptyState();
  addEvents(st, [event("a", "2026-11-19T10:00:00Z", summary({ forecast: { checked: 1, matched: 0, skipped: 0, skipReasons: {}, mismatches: { "it's --> odd": 1 } } }))]);
  const body = render(st, new Date("2026-11-19T12:00:00Z"));
  assert.match(body, /\| \*\*Clean turns\*\* \| \*\*80\.0%\*\* \|/);
  const back = decodeState(body);
  assert.deepEqual(back, JSON.parse(JSON.stringify(st)));
  assert.deepEqual(decodeState("no state here"), emptyState());
});

test("pruning keeps the newest versions and forgets old event ids", () => {
  const st = emptyState();
  for (let i = 0; i < 20; i++) addEvents(st, [event(`v${i}`, "2026-11-19T10:00:00Z", summary({ mod: `1.20.${i}` }))]);
  prune(st, Date.parse("2026-11-19T20:00:00Z"));
  const versions = Object.keys(st.versions);
  assert.equal(versions.length, 12);
  assert.ok(versions.includes("1.20.19") && !versions.includes("1.20.0"));
  assert.deepEqual(st.recent, {});
});

test("a run counts new events and rewrites the issue", async () => {
  const st = emptyState();
  addEvents(st, [event("old", "2026-11-19T08:00:00Z", summary())]);
  const issue = { number: 7, node_id: "N", body: render(st) };
  const calls = [];
  const github = async (path, init) => {
    calls.push([init?.method || "GET", path]);
    if (path.includes("labels=session-metrics")) return [issue];
    if (init?.method === "PATCH") { issue.body = JSON.parse(init.body).body; return issue; }
    return null;
  };
  const sentry = async () => [{ id: "42" }];
  async function* sentryPages(path) {
    assert.match(path, /issues\/42\/events\/.*start=2026-11-19T06%3A00%3A00\.000Z/, "starts two hours before the newest counted event");
    yield [event("old", "2026-11-19T08:00:00Z", summary()), event("new", "2026-11-19T09:00:00Z", summary())];
  }
  await syncSessionMetrics({ ORG: "o", PROJECT: "p", REPO: "r/r", sentry, sentryPages, github, graphql: async () => ({}), log: () => {} });
  assert.equal(decodeState(issue.body).versions["1.20.14"].sessions, 2);
  assert.ok(calls.some(([m]) => m === "PATCH"));
});

test("adds the fights' frames against a 165 fps frame's budget, and keeps older totals that had none", () => {
  const st = emptyState();
  const fightFrames = (overBudget, longestRun, longestRunBy) => ({ frames: 6000, seconds: 100, overBudget, longestRun, longestRunBy, overBudgetBy: { "turn-forecast": overBudget } });
  addEvents(st, [event("g", "2026-11-27T10:00:00Z", summary({ mod: "2.0.2", fightFrames: fightFrames(20, 3, "card-tabs") })),
    event("h", "2026-11-27T11:00:00Z", summary({ mod: "2.0.2", fightFrames: fightFrames(10, 7, "turn-forecast") }))]);
  const f = st.versions["2.0.2"].fightFrames;
  assert.deepEqual([f.frames, f.seconds, f.overBudget, f.longestRun, f.longestRunBy, f.by["turn-forecast"]], [12000, 200, 30, 7, "turn-forecast", 30]);
  const body = render(prune(st));
  assert.ok(body.includes("| 60 fps |"));
  assert.ok(body.includes("| 9.0 |"), "30 frames over in 200 seconds: 9 a minute");
  const old = emptyState();
  addEvents(old, [event("i", "2026-11-27T10:00:00Z", summary())]);
  delete old.versions["1.20.14"].fightFrames; // kept by a sync from before the field
  addEvents(old, [event("j", "2026-11-27T12:00:00Z", summary({ fightFrames: fightFrames(5, 2, "card-tabs") }))]);
  assert.equal(old.versions["1.20.14"].fightFrames.overBudget, 5);
});
