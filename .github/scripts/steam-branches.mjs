// Watches Slay the Spire 2's Steam branches (run by .github/workflows/steam-branches.yml).
//
//  - Each branch's build (public = main, public-beta = beta) is read from Steam's public app info, as SteamCMD gives it to an
//    anonymous login (served as JSON by api.steamcmd.net), and the version names from Steam's own news feed (the patch notes).
//  - A tracking issue labelled steam-branches holds the builds last seen (a hidden state comment) and a table of them.
//  - A branch whose build changed opens an issue labelled steam-update, with the new build, the patch notes since, and the
//    steps the change needs (docs/DEVELOPMENT.md "When main updates" for main, "After a game patch" for beta). GitHub's own
//    notifications bring it to your inbox.
//  - The first run only records the builds.
//
// Env: GITHUB_TOKEN, GITHUB_REPOSITORY (owner/repo).

export const APP = 2868840;
export const BRANCHES = { public: "main", "public-beta": "beta" };
const STATE_MARK = "steam-branches-state";

/** The builds in an app info response: { public: { build, updated }, ... } for the watched branches. */
export function buildsOf(info) {
  const branches = info?.data?.[APP]?.depots?.branches;
  if (!branches) throw new Error("no branches in the app info");
  const builds = {};
  for (const name of Object.keys(BRANCHES)) {
    const branch = branches[name];
    if (!branch?.buildid) throw new Error(`branch ${name} missing from the app info`);
    builds[name] = { build: String(branch.buildid), updated: Number(branch.timeupdated || branch.timebuildupdated || 0) };
  }
  return builds;
}

/** The branches whose build differs from the recorded one (none on the first run: nothing recorded). */
export function changes(recorded, current) {
  if (!recorded) return [];
  return Object.keys(BRANCHES)
    .filter((name) => recorded[name]?.build !== current[name].build)
    .map((name) => ({ name, from: recorded[name]?.build ?? "?", to: current[name].build, updated: current[name].updated, since: recorded[name]?.updated ?? 0 }));
}

/**
 * The patch notes posted since a time (a day before it, so notes posted just before the build count), newest first; with a
 * branch, the ones whose title names it (beta notes say "Beta", main's don't), or all of them when none does.
 */
export function notesSince(news, since, branch) {
  const notes = (news?.appnews?.newsitems ?? [])
    .filter((item) => item.feedname === "steam_community_announcements" && item.date >= since - 86400)
    .map((item) => ({ title: item.title, date: item.date, url: item.url }));
  if (!branch) return notes;
  const beta = BRANCHES[branch] === "beta";
  const named = notes.filter((note) => /\bbeta\b/i.test(note.title) === beta);
  return named.length ? named : notes;
}

export function stateComment(builds) {
  return `<!-- ${STATE_MARK} ${Buffer.from(JSON.stringify(builds)).toString("base64")} -->`;
}

export function readState(body) {
  const match = (body || "").match(new RegExp(`<!-- ${STATE_MARK} ([A-Za-z0-9+/=]+) -->`));
  return match ? JSON.parse(Buffer.from(match[1], "base64").toString("utf8")) : null;
}

const day = (seconds) => (seconds ? new Date(seconds * 1000).toISOString().slice(0, 16).replace("T", " ") + " UTC" : "?");

export function trackingBody(builds) {
  let body = "The game's Steam branches as last seen by the Steam branches workflow (every 6 hours). A change opens an issue labelled `steam-update`.\n\n";
  body += "| Branch | Steam branch | Build | Updated |\n|---|---|---|---|\n";
  for (const [name, label] of Object.entries(BRANCHES)) body += `| ${label} | \`${name}\` | ${builds[name].build} | ${day(builds[name].updated)} |\n`;
  return body + "\n" + stateComment(builds) + "\n";
}

export function updateTitle(change) {
  return `Steam ${BRANCHES[change.name]} updated: build ${change.from} → ${change.to}`;
}

export function updateBody(change, notes) {
  const main = BRANCHES[change.name] === "main";
  let body = `The game's \`${change.name}\` branch (${BRANCHES[change.name]}) has a new build: ${change.from} → **${change.to}**, updated ${day(change.updated)}.\n\n`;
  body += "### Patch notes since the last build\n";
  body += notes.length ? notes.map((note) => `- ${day(note.date).slice(0, 10)}: [${note.title}](${note.url})`).join("\n") + "\n" : "- none posted yet (a hotfix may come without notes)\n";
  body += main
    ? "\n### Steps (docs/DEVELOPMENT.md \"When main updates\")\n" +
      "- [ ] Switch the game to main in Steam, `./scripts/decompile.ps1 -Branch main`, switch back to beta\n" +
      "- [ ] `node scripts/game-names.mjs`\n" +
      "- [ ] `dotnet test tests/SpireQoL.Catalog.Tests`: take off every gate main has caught up on; gate anything main now lacks\n" +
      "- [ ] Diff the old and new main decompiles for what else the mod reads\n" +
      "- [ ] Diff the game methods the method list names (`Snapshots/hand-modelled.txt`): each change is a hand-kept rule to update\n" +
      "- [ ] Set `MainReviewed` in GameBranchTests to the new version\n"
    : "\n### Steps (docs/DEVELOPMENT.md \"After a game patch\")\n" +
      "- [ ] Let Steam update the game, `./scripts/decompile.ps1`\n" +
      "- [ ] `node scripts/game-names.mjs` and `node scripts/coverage-ledger.mjs`; review every class and relic it lists\n" +
      "- [ ] Diff the game methods the method list names (`Snapshots/hand-modelled.txt`): each change is a hand-kept rule to update\n" +
      "- [ ] Rebuild, `./scripts/check.ps1`\n";
  return body + "\nClose this once the steps are done.\n";
}

async function main() {
  const token = process.env.GITHUB_TOKEN;
  const repo = process.env.GITHUB_REPOSITORY;
  if (!token || !repo) throw new Error("GITHUB_TOKEN and GITHUB_REPOSITORY are needed");
  const github = async (path, init = {}) => {
    const response = await fetch(`https://api.github.com${path}`, {
      ...init,
      headers: { Authorization: `Bearer ${token}`, Accept: "application/vnd.github+json", "Content-Type": "application/json", ...(init.headers || {}) },
    });
    if (!response.ok) throw new Error(`GitHub ${init.method || "GET"} ${path}: ${response.status} ${await response.text()}`);
    return response.status === 204 ? null : response.json();
  };
  const json = async (url) => {
    const response = await fetch(url, { headers: { "User-Agent": "sts2-mods steam-branches workflow" } });
    if (!response.ok) throw new Error(`${url}: ${response.status}`);
    return response.json();
  };
  for (const [name, color] of [["steam-branches", "5319e7"], ["steam-update", "d93f0b"]]) {
    try { await github(`/repos/${repo}/labels/${name}`); }
    catch { await github(`/repos/${repo}/labels`, { method: "POST", body: JSON.stringify({ name, color }) }); }
  }

  const current = buildsOf(await json(`https://api.steamcmd.net/v1/info/${APP}`));
  const tracking = (await github(`/repos/${repo}/issues?labels=steam-branches&state=all&per_page=10`)).find((issue) => !issue.pull_request);
  const recorded = tracking ? readState(tracking.body) : null;
  const found = changes(recorded, current);
  console.log(`builds: ${Object.entries(current).map(([name, branch]) => `${name} ${branch.build}`).join(", ")}; ${recorded ? `${found.length} changed` : "first run: recorded"}`);

  if (found.length) {
    const news = await json(`https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid=${APP}&count=15&maxlength=1&feeds=steam_community_announcements`);
    for (const change of found) {
      const issue = await github(`/repos/${repo}/issues`, {
        method: "POST",
        body: JSON.stringify({ title: updateTitle(change), body: updateBody(change, notesSince(news, change.since, change.name)), labels: ["steam-update"] }),
      });
      console.log(`opened #${issue.number}: ${updateTitle(change)}`);
    }
  }
  const body = trackingBody(current);
  if (!tracking) {
    const issue = await github(`/repos/${repo}/issues`, { method: "POST", body: JSON.stringify({ title: "Steam branches (watched)", body, labels: ["steam-branches"] }) });
    console.log(`opened the tracking issue #${issue.number}`);
  } else if (tracking.body !== body) {
    await github(`/repos/${repo}/issues/${tracking.number}`, { method: "PATCH", body: JSON.stringify({ body, state: "open" }) });
  }
}

if (import.meta.url === `file://${process.argv[1]}` || process.argv[1]?.endsWith("steam-branches.mjs")) {
  main().catch((error) => {
    console.error(error.message);
    process.exit(1);
  });
}
