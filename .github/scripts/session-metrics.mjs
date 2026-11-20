// Session metrics (decision #108): players' games send one "Session summary" event per play session (SessionWatch in the mod,
// fingerprint "session", tag kind:session, the counts as JSON in the "summary" extra). The Sentry sync calls syncSessionMetrics
// every hour: it reads the events it hasn't counted yet, adds them to running totals per mod version, and rewrites one pinned
// GitHub issue (label session-metrics) with a table. The totals live in that issue's body (a hidden, base64 block), so they outlast
// Sentry's retention and nothing else needs storing. Delete the issue to start counting again from what Sentry still has.
//
// The pure parts (add, render, the state block) are tested by session-metrics.test.mjs: `node --test .github/scripts/session-metrics.test.mjs`.

export const LABEL = "session-metrics";
const STATE_RE = /<!-- session-metrics-state\n([A-Za-z0-9+/=\n]*)\n-->/;
/** Event ids remembered this long after their date, so the overlap between runs (late-indexed events) isn't counted twice. */
const REMEMBER_MS = 3 * 3600_000;
/** Each run looks back this far before the newest event it counted: Sentry can take a while to make an event searchable. */
const OVERLAP_MS = 2 * 3600_000;
/** Reason lists keep this many per version (the most frequent). */
const KEEP_REASONS = 30;
/** Versions kept in the totals (the newest). */
const KEEP_VERSIONS = 12;
/** Versions that keep their full reason lists (the newest); older ones keep their top few, to keep the issue body small. */
const FULL_REASON_VERSIONS = 3;
/** GitHub's limit for an issue body is 65,536 characters: oldest versions are dropped until it fits under this. */
const MAX_BODY = 60_000;
/** Versions shown in the table (the newest). */
const SHOW_VERSIONS = 5;

// ---------------------------------------------------------------- pure: totals

export const emptyState = () => ({ v: 1, cursor: null, recent: {}, versions: {} });

const audit = () => ({ checked: 0, matched: 0, skipped: 0, skipReasons: {}, mismatches: {} });
const timing = () => ({ sessions: 0, count: 0, p95Sum: 0, p95Max: 0, max: 0 });

export function emptyTotals() {
  return {
    sessions: 0, ended: {}, multiplayer: 0, otherMods: 0, minutes: 0, cores: {},
    combats: 0, turns: 0, clean: 0, unchecked: 0, capped: 0, mismatched: 0,
    forecast: audit(), plays: audit(),
    // The audits outside the turns (intent, potion, reminders, rest), by name: summaries from 1.21.10 on.
    others: {},
    searches: { count: 0, background: 0, cancelled: 0, copies: 0, cancelledCopies: 0, maxHeld: 0, slowestMs: 0, slowest: "", caps: {}, ms: timing() },
    waits: { missed: 0, ms: timing() },
    unsettled: timing(),
    frames: { slow: 0, gameStalls: 0, ms: timing() },
    gc: { gen0: 0, gen1: 0, gen2: 0, pauseMs: 0, allocatedMb: 0 },
    reports: {},
  };
}

const addCounts = (into, from) => { for (const [k, v] of Object.entries(from || {})) into[k] = (into[k] || 0) + (Number(v) || 0); };
const num = (v) => Number(v) || 0;

function addTiming(into, t) {
  if (!t || !num(t.count)) return;
  into.sessions++;
  into.count += num(t.count);
  into.p95Sum += num(t.p95);
  into.p95Max = Math.max(into.p95Max, num(t.p95));
  into.max = Math.max(into.max, num(t.max));
}

function addAudit(into, a) {
  if (!a) return;
  into.checked += num(a.checked);
  into.matched += num(a.matched);
  into.skipped += num(a.skipped);
  addCounts(into.skipReasons, a.skipReasons);
  addCounts(into.mismatches, a.mismatches);
}

/** Adds one session's summary (the mod's SessionSummary, format 1) to a version's totals. */
export function addSummary(t, s) {
  t.sessions++;
  t.ended[s.ended || "?"] = (t.ended[s.ended || "?"] || 0) + 1;
  if (num(s.players) > 1) t.multiplayer++;
  if (s.otherMods) t.otherMods++;
  t.minutes += num(s.minutes);
  t.cores[String(s.cores || "?")] = (t.cores[String(s.cores || "?")] || 0) + 1;
  for (const k of ["combats", "turns", "clean", "unchecked", "capped", "mismatched"]) t[k] += num(s[k]);
  addAudit(t.forecast, s.forecast);
  addAudit(t.plays, s.plays);
  t.others ||= {}; // a version's totals kept before the field existed
  for (const [name, a] of Object.entries(s.others || {})) addAudit(t.others[name] ||= audit(), a);
  const q = s.searches || {};
  for (const k of ["count", "background", "cancelled", "copies", "cancelledCopies"]) t.searches[k] += num(q[k]);
  t.searches.maxHeld = Math.max(t.searches.maxHeld, num(q.maxHeld));
  if (num(q.ms?.max) > t.searches.slowestMs) { t.searches.slowestMs = num(q.ms.max); t.searches.slowest = String(q.slowest || ""); }
  addCounts(t.searches.caps, q.caps);
  addTiming(t.searches.ms, q.ms);
  t.waits.missed += num(s.waits?.missed);
  addTiming(t.waits.ms, s.waits?.ms);
  addTiming(t.unsettled, s.unsettled);
  t.frames.slow += num(s.frames?.slow);
  t.frames.gameStalls += num(s.frames?.gameStalls);
  addTiming(t.frames.ms, s.frames?.modMs);
  for (const k of ["gen0", "gen1", "gen2", "pauseMs", "allocatedMb"]) t.gc[k] += num(s.gc?.[k]);
  addCounts(t.reports, s.reports);
}

const top = (o, n) => Object.fromEntries(Object.entries(o).sort((a, b) => b[1] - a[1] || (a[0] < b[0] ? -1 : 1)).slice(0, n));

/** Keeps the state small enough for an issue body: the newest versions, the top reasons, recent ids only. */
export function prune(state, now = Date.now()) {
  const versions = Object.keys(state.versions).sort(cmpVersion).reverse();
  for (const v of versions.slice(KEEP_VERSIONS)) delete state.versions[v];
  versions.slice(0, KEEP_VERSIONS).forEach((v, i) => {
    const t = state.versions[v];
    const n = i < FULL_REASON_VERSIONS ? KEEP_REASONS : 5;
    for (const a of [t.forecast, t.plays, ...Object.values(t.others || {})]) { a.skipReasons = top(a.skipReasons, n); a.mismatches = top(a.mismatches, n); }
    t.searches.caps = top(t.searches.caps, n);
    t.reports = top(t.reports, n);
  });
  for (const [id, at] of Object.entries(state.recent)) if (now - Date.parse(at) > REMEMBER_MS) delete state.recent[id];
  return state;
}

/**
 * Adds a page of Sentry events (newest first or not) to the state. Each event: { eventID, dateCreated, tags: [{key, value}],
 * context: { summary } }. Skips events already counted (state.recent), from a dev's own game (tag dev=true), and summaries it
 * can't read or of a format it doesn't know. Returns how many it added.
 */
export function addEvents(state, events) {
  let added = 0;
  for (const e of events) {
    const id = String(e.eventID || e.id || "").slice(0, 12);
    if (!id || state.recent[id]) continue;
    state.recent[id] = e.dateCreated;
    if (!state.cursor || Date.parse(e.dateCreated) > Date.parse(state.cursor)) state.cursor = e.dateCreated;
    const tag = (k) => (e.tags || []).find((t) => t.key === k)?.value;
    if (tag("dev") === "true") continue;
    let s = e.context?.summary;
    try { if (typeof s === "string") s = JSON.parse(s); } catch { s = null; }
    if (!s || s.v !== 1) continue;
    const version = s.mod || tag("mod_version") || "?";
    state.versions[version] ??= emptyTotals();
    addSummary(state.versions[version], s);
    added++;
  }
  return added;
}

// ---------------------------------------------------------------- pure: the issue body

export const encodeState = (state) => Buffer.from(JSON.stringify(state)).toString("base64").replace(/(.{100})/g, "$1\n");

export function decodeState(body) {
  const m = STATE_RE.exec(body || "");
  if (!m) return emptyState();
  try {
    const s = JSON.parse(Buffer.from(m[1].replace(/\n/g, ""), "base64").toString("utf8"));
    return s?.v === 1 ? { ...emptyState(), ...s } : emptyState();
  } catch {
    return emptyState();
  }
}

const parseVersion = (v) => (/^(\d+)\.(\d+)\.(\d+)/.exec(String(v || "")) || []).slice(1).map(Number);
export function cmpVersion(a, b) {
  const x = parseVersion(a), y = parseVersion(b);
  if (x.length < 3 || y.length < 3) return x.length - y.length;
  for (let i = 0; i < 3; i++) if (x[i] !== y[i]) return x[i] - y[i];
  return 0;
}

const pct = (n, d) => (d ? `${((100 * n) / d).toFixed(1)}%` : "–");
const ms = (v) => (v ? `${Math.round(v * 10) / 10} ms` : "–");
const meanP95 = (t) => (t.sessions ? `${ms(t.p95Sum / t.sessions)} (worst ${ms(t.p95Max)})` : "–");
const perHour = (n, minutes) => (minutes ? (n / (minutes / 60)).toFixed(1) : "–");
const list = (o, n = 8) => {
  const e = Object.entries(o).sort((a, b) => b[1] - a[1]).slice(0, n);
  return e.length ? e.map(([k, v]) => `${k.replace(/\|/g, "/")} ×${v}`).join("<br>") : "–";
};

/** The issue's body: a table of the newest versions, the newest version's reasons, and the hidden totals. */
export function render(state, now = new Date()) {
  const versions = Object.keys(state.versions).sort(cmpVersion).reverse().slice(0, SHOW_VERSIONS);
  let b = `Play sessions from players' games (Spire QoL with **Send error reports** on): one summary per session that had a fight turn, `;
  b += `added up per mod version every hour by the Sentry sync (\`.github/scripts/session-metrics.mjs\`, decision #108; what each row means: docs/DEVELOPMENT.md "Session metrics"). `;
  b += `Updated ${now.toISOString().slice(0, 16).replace("T", " ")} UTC.\n\n`;
  if (!versions.length) return b + "No sessions yet.\n\n" + `<!-- session-metrics-state\n${encodeState(state)}\n-->\n`;
  const rows = [
    ["Sessions", (t) => `${t.sessions}${t.ended.unseen ? ` (${t.ended.unseen} unseen ends)` : ""}`],
    ["Play time", (t) => `${(t.minutes / 60).toFixed(1)} h`],
    ["Multiplayer / other mods", (t) => `${pct(t.multiplayer, t.sessions)} / ${pct(t.otherMods, t.sessions)}`],
    ["Fights / turns", (t) => `${t.combats} / ${t.turns}`],
    ["**Clean turns**", (t) => `**${pct(t.clean, t.turns)}**`],
    ["Unchecked turns", (t) => pct(t.unchecked, t.turns)],
    ["Capped turns", (t) => pct(t.capped, t.turns)],
    ["Mismatched turns", (t) => pct(t.mismatched, t.turns)],
    ["Forecasts matched / checked (skipped)", (t) => `${t.forecast.matched} / ${t.forecast.checked} (${t.forecast.skipped})`],
    ["Plays matched / checked (checks skipped)", (t) => `${t.plays.matched} / ${t.plays.checked} (${t.plays.skipped})`],
    ["Other checks matched / checked (skipped)", (t) => Object.entries(t.others || {}).sort().map(([k, a]) => `${k} ${a.matched} / ${a.checked} (${a.skipped})`).join(", ") || "–"],
    ["Searches: background / cancelled", (t) => `${pct(t.searches.background, t.searches.count)} / ${pct(t.searches.cancelled, t.searches.count)}`],
    ["Search time p95", (t) => meanP95(t.searches.ms)],
    ["Slowest search", (t) => `${ms(t.searches.slowestMs)}${t.searches.slowest ? ` (${t.searches.slowest.replace(/\|/g, "/").slice(0, 40)})` : ""}`],
    ["Most states held", (t) => `${t.searches.maxHeld}`],
    ["Caps hit", (t) => list(t.searches.caps)],
    ["Frame waits left to the background", (t) => pct(t.waits.missed, t.waits.ms.count)],
    ["Board settling p95 (when not in its frame)", (t) => meanP95(t.unsettled)],
    ["Mod frame time p95", (t) => meanP95(t.frames.ms)],
    ["Slow mod frames (>30 ms) per hour", (t) => perHour(t.frames.slow, t.minutes)],
    ["Game's own stalls per hour", (t) => perHour(t.frames.gameStalls, t.minutes)],
    ["Gen 2 collections per hour (process)", (t) => perHour(t.gc.gen2, t.minutes)],
    ["Reports sent per session", (t) => (t.sessions ? (Object.values(t.reports).reduce((a, n) => a + n, 0) / t.sessions).toFixed(2) : "–")],
    ["Ended", (t) => list(t.ended)],
  ];
  b += `| | ${versions.join(" | ")} |\n|---|${versions.map(() => "---").join("|")}|\n`;
  for (const [name, f] of rows) b += `| ${name} | ${versions.map((v) => f(state.versions[v])).join(" | ")} |\n`;
  const t = state.versions[versions[0]];
  b += `\n### ${versions[0]}: why checks were skipped, what didn't match\n\n`;
  b += `| | |\n|---|---|\n`;
  b += `| Forecast checks skipped | ${list(t.forecast.skipReasons, 12)} |\n`;
  b += `| Play checks skipped | ${list(t.plays.skipReasons, 12)} |\n`;
  b += `| Forecast mismatches | ${list(t.forecast.mismatches, 12)} |\n`;
  b += `| Play mismatches | ${list(t.plays.mismatches, 12)} |\n`;
  for (const [k, a] of Object.entries(t.others || {}).sort()) {
    if (a.skipped) b += `| ${k} checks skipped | ${list(a.skipReasons, 12)} |\n`;
    if (a.checked > a.matched) b += `| ${k} mismatches | ${list(a.mismatches, 12)} |\n`;
  }
  b += `| Reports sent | ${list(t.reports, 12)} |\n`;
  b += `\n<!-- session-metrics-state\n${encodeState(state)}\n-->\n`;
  return b;
}

// ---------------------------------------------------------------- the run

/**
 * Reads the session events not yet counted and rewrites the pinned issue. `sentryPages(path)` yields pages of a Sentry list (it
 * follows the Link header); `github(path, init)` calls the GitHub API; `graphql(query, vars)` its GraphQL API (to pin the issue).
 */
export async function syncSessionMetrics({ ORG, PROJECT, REPO, sentry, sentryPages, github, graphql, log = console.log }) {
  const found = await github(`/repos/${REPO}/issues?labels=${LABEL}&state=all&per_page=1`);
  let issue = found[0] ?? null;
  const state = decodeState(issue?.body);
  // Sentry's issue search takes only '', 24h or 14d as a period: a session issue seen in the last 14 days is found (sessions come in
  // all the time; with none for two weeks there's nothing new to count).
  const sessionIssues = await sentry(`/projects/${ORG}/${PROJECT}/issues/?query=${encodeURIComponent("kind:session")}&statsPeriod=14d&limit=25`);
  const range = state.cursor
    ? `start=${encodeURIComponent(new Date(Date.parse(state.cursor) - OVERLAP_MS).toISOString())}&end=${encodeURIComponent(new Date().toISOString())}`
    : `start=${encodeURIComponent(new Date(Date.now() - 89 * 86400_000).toISOString())}&end=${encodeURIComponent(new Date().toISOString())}`; // the first run: what Sentry keeps (90 days)
  let added = 0, pages = 0;
  for (const si of sessionIssues || []) {
    for await (const page of sentryPages(`/organizations/${ORG}/issues/${si.id}/events/?full=true&per_page=100&${range}`)) {
      added += addEvents(state, page);
      if (++pages >= 100) { log("session metrics: stopped after 100 pages of events; the rest are counted next run"); break; }
    }
  }
  prune(state);
  let body = render(state);
  for (const v of Object.keys(state.versions).sort(cmpVersion)) {
    if (body.length <= MAX_BODY) break;
    delete state.versions[v];
    log(`session metrics: dropped ${v}'s totals to keep the issue under GitHub's size limit`);
    body = render(state);
  }
  if (!issue) {
    try { await github(`/repos/${REPO}/labels`, { method: "POST", body: JSON.stringify({ name: LABEL, color: "0e8a16" }) }); } catch { /* exists */ }
    issue = await github(`/repos/${REPO}/issues`, { method: "POST", body: JSON.stringify({ title: "Session metrics (from players' games)", body, labels: [LABEL] }) });
    try { await graphql(`mutation($id: ID!) { pinIssue(input: { issueId: $id }) { issue { number } } }`, { id: issue.node_id }); }
    catch (e) { log(`session metrics: couldn't pin #${issue.number} (${e.message}); pin it by hand`); }
    log(`session metrics: created #${issue.number}`);
  } else {
    await github(`/repos/${REPO}/issues/${issue.number}`, { method: "PATCH", body: JSON.stringify({ body }) });
  }
  log(`session metrics: ${added} new session(s) counted in #${issue.number}`);
}
