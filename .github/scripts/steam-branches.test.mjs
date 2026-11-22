// node --test .github/scripts/steam-branches.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { APP, buildsOf, changes, notesSince, readState, stateComment, trackingBody, updateBody, updateTitle } from "./steam-branches.mjs";

const info = (publicBuild, betaBuild) => ({
  data: { [APP]: { depots: { branches: {
    public: { buildid: publicBuild, timeupdated: "1794877497" },
    "public-beta": { buildid: betaBuild, timeupdated: "1794689668" },
    other: { buildid: "1" },
  } } } },
});

test("reads the watched branches' builds", () => {
  const builds = buildsOf(info("26655341", "26660232"));
  assert.deepEqual(builds, { public: { build: "26655341", updated: 1794877497 }, "public-beta": { build: "26660232", updated: 1794689668 } });
});

test("a missing branch fails instead of reading as a change", () => {
  assert.throws(() => buildsOf({ data: { [APP]: { depots: { branches: { public: { buildid: "1" } } } } } }), /public-beta/);
});

test("the first run records without reporting", () => {
  assert.deepEqual(changes(null, buildsOf(info("1", "2"))), []);
});

test("only a branch whose build changed is reported", () => {
  const recorded = buildsOf(info("1", "2"));
  const found = changes(recorded, buildsOf(info("1", "3")));
  assert.equal(found.length, 1);
  assert.equal(found[0].name, "public-beta");
  assert.equal(updateTitle(found[0]), "Steam beta updated: build 2 → 3");
  assert.match(updateBody(found[0], []), /After a game patch/);
  assert.match(updateBody({ ...found[0], name: "public" }, []), /When main updates/);
});

test("the state survives the tracking issue's body", () => {
  const builds = buildsOf(info("26655341", "26660232"));
  assert.deepEqual(readState(trackingBody(builds)), builds);
  assert.deepEqual(readState("text " + stateComment(builds)), builds);
  assert.equal(readState("no state here"), null);
});

test("patch notes since the last build, announcements only", () => {
  const news = { appnews: { newsitems: [
    { title: "Major Update #3 - v0.111.1", date: 1794877000, feedname: "steam_community_announcements", url: "u1" },
    { title: "A review", date: 1794877000, feedname: "PCGamesN", url: "u2" },
    { title: "Beta Patch Notes - v0.113.0", date: 1794000000, feedname: "steam_community_announcements", url: "u3" },
  ] } };
  assert.deepEqual(notesSince(news, 1794800000).map((note) => note.title), ["Major Update #3 - v0.111.1"]);
  assert.deepEqual(notesSince(news, 1793900000, "public").map((note) => note.title), ["Major Update #3 - v0.111.1"]);
  assert.deepEqual(notesSince(news, 1793900000, "public-beta").map((note) => note.title), ["Beta Patch Notes - v0.113.0"]);
  assert.deepEqual(notesSince(news, 1794800000, "public-beta").map((note) => note.title), ["Major Update #3 - v0.111.1"]); // none named: all
});
