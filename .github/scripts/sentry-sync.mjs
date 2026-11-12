// Mirrors the mod's Sentry issues to GitHub issues (run by .github/workflows/sentry-issues.yml).
//
//  - New unresolved Sentry issue        → new GitHub issue labelled auto-report + crash/mismatch,
//                                         with the full report (stack trace, game state, logs); a
//                                         report from a version older than the newest release says so.
//  - GitHub issue closed (fixed)        → Sentry issue resolved (in the fixing release when known).
//  - Happened again after being closed  → only from versions older than the fix (players who haven't
//                                         updated): stays closed, resolved again in Sentry. From the
//                                         fixing version or newer, or with no fix version: GitHub issue
//                                         reopened with a comment, and its body replaced with the newest
//                                         occurrence. The fix version is the newest "spireqol-vX.Y.Z" (or
//                                         "Spire QoL X.Y.Z") in the latest closing comment that names one.
// A hidden marker in each issue body (<!-- sentry-issue: ID -->) links the two.
//
// Env: SENTRY_AUTH_TOKEN (secret), SENTRY_ORG, SENTRY_PROJECT, SENTRY_API (default https://sentry.io),
//      GITHUB_TOKEN, GITHUB_REPOSITORY (owner/repo).

const env = (k, d) => process.env[k] || d;
const SENTRY = env("SENTRY_API", "https://sentry.io").replace(/\/$/, "");
const ORG = env("SENTRY_ORG");
const PROJECT = env("SENTRY_PROJECT");
const STOKEN = env("SENTRY_AUTH_TOKEN");
const GTOKEN = env("GITHUB_TOKEN");
const REPO = env("GITHUB_REPOSITORY");
if (!ORG || !PROJECT || !STOKEN) {
  console.log("Sentry isn't configured yet (SENTRY_ORG / SENTRY_PROJECT / SENTRY_AUTH_TOKEN); nothing to do.");
  process.exit(0);
}

async function sentry(path, init = {}) {
  const r = await fetch(`${SENTRY}/api/0${path}`, { ...init, headers: { Authorization: `Bearer ${STOKEN}`, "Content-Type": "application/json", ...(init.headers || {}) } });
  if (!r.ok) {
    const err = new Error(`Sentry ${init.method || "GET"} ${path}: ${r.status} ${await r.text()}`);
    err.status = r.status;
    throw err;
  }
  return r.status === 204 ? null : r.json();
}

// The newest event of an issue. A brand-new issue can 404 on "latest" for a few minutes while
// Sentry indexes it; then try the event list, and return null (retry next run) if that's empty too.
async function latestEvent(id) {
  try {
    return await sentry(`/organizations/${ORG}/issues/${id}/events/latest/`);
  } catch (e) {
    if (e.status !== 404) throw e;
  }
  const list = await sentry(`/organizations/${ORG}/issues/${id}/events/?full=true&per_page=1`);
  return list?.[0] ?? null;
}

async function github(path, init = {}) {
  const r = await fetch(`https://api.github.com${path}`, { ...init, headers: { Authorization: `Bearer ${GTOKEN}`, Accept: "application/vnd.github+json", "Content-Type": "application/json", ...(init.headers || {}) } });
  if (!r.ok) throw new Error(`GitHub ${init.method || "GET"} ${path}: ${r.status} ${await r.text()}`);
  return r.status === 204 ? null : r.json();
}

async function ensureLabels() {
  const want = { "auto-report": "d93f0b", crash: "b60205", mismatch: "fbca04" };
  for (const [name, color] of Object.entries(want)) {
    try { await github(`/repos/${REPO}/labels/${encodeURIComponent(name)}`); }
    catch { await github(`/repos/${REPO}/labels`, { method: "POST", body: JSON.stringify({ name, color }) }); }
  }
}

async function mirroredIssues() {
  const map = new Map(); // sentry id → GitHub issue
  for (let page = 1; page < 20; page++) {
    const list = await github(`/repos/${REPO}/issues?labels=auto-report&state=all&per_page=100&page=${page}`);
    for (const i of list) {
      const m = /<!-- sentry-issue: (\d+) -->/.exec(i.body || "");
      if (m) map.set(m[1], i);
    }
    if (list.length < 100) break;
  }
  return map;
}

// ---- versions: "0.31.17" style, compared numerically
const parseVersion = (v) => (/^(\d+)\.(\d+)\.(\d+)/.exec(String(v || "")) || []).slice(1).map(Number);
const cmpVersion = (a, b) => {
  const x = parseVersion(a), y = parseVersion(b);
  if (x.length < 3 || y.length < 3) return NaN;
  for (let i = 0; i < 3; i++) if (x[i] !== y[i]) return x[i] - y[i];
  return 0;
};
const tagOf = (event, key) => (event.tags || []).find((t) => t.key === key)?.value;

// The version that fixed a closed issue: the newest version named in the latest comment that names one.
async function fixedIn(number) {
  const comments = await github(`/repos/${REPO}/issues/${number}/comments?per_page=100`);
  for (const c of comments.slice().reverse()) {
    const found = [...(c.body || "").matchAll(/(?:spireqol-v|Spire QoL )(\d+\.\d+\.\d+)/gi)].map((m) => m[1]);
    if (found.length) return found.sort(cmpVersion).at(-1);
  }
  return null;
}

// Events of an issue after a date, newest first (with tags).
async function eventsSince(id, since) {
  const list = await sentry(`/organizations/${ORG}/issues/${id}/events/?full=true&per_page=100`);
  return (list || []).filter((e) => new Date(e.dateCreated) > new Date(since));
}

async function resolve(id, version) {
  if (version) {
    try {
      await sentry(`/organizations/${ORG}/issues/${id}/`, { method: "PUT", body: JSON.stringify({ status: "resolved", statusDetails: { inRelease: `spireqol@${version}` } }) });
      return;
    } catch (e) {
      console.log(`Sentry ${id}: couldn't resolve in release ${version} (${e.status}); resolving plainly`);
    }
  }
  await sentry(`/organizations/${ORG}/issues/${id}/`, { method: "PUT", body: JSON.stringify({ status: "resolved" }) });
}

let newestRelease = null;
async function loadNewestRelease() {
  try {
    const list = await github(`/repos/${REPO}/releases?per_page=100`);
    newestRelease = list.filter((r) => !r.draft && !r.prerelease && /^spireqol-v/i.test(r.tag_name))
      .map((r) => r.tag_name.replace(/^spireqol-v/i, "")).sort(cmpVersion).at(-1) ?? null;
  } catch (e) {
    console.log(`couldn't read releases: ${e.message}`);
  }
}

const clip = (s, n) => (s && s.length > n ? s.slice(0, n) + `\n… (${s.length - n} more characters)` : s || "");
const block = (title, text, n = 6000) => (text ? `\n### ${title}\n\n\`\`\`\n${clip(String(text), n)}\n\`\`\`\n` : "");

function stackTrace(event) {
  const ex = (event.entries || []).find((e) => e.type === "exception");
  if (!ex) return "";
  return ex.data.values.map((v) => {
    const frames = (v.stacktrace?.frames || []).slice().reverse().slice(0, 30)
      .map((f) => `  at ${f.module ? f.module + "." : ""}${f.function || "?"}${f.filename ? ` (${f.filename}:${f.lineNo ?? "?"})` : ""}`);
    return `${v.type}: ${v.value}\n${frames.join("\n")}`;
  }).join("\n\n");
}

function body(issue, event) {
  const tags = Object.fromEntries((event.tags || []).map((t) => [t.key, t.value]));
  const extra = event.context || {};
  const kind = tags.kind || "crash";
  let b = `<!-- sentry-issue: ${issue.id} -->\n`;
  b += `**Automatic report** from Spire QoL players (${kind}). [Sentry issue](${issue.permalink})\n\n`;
  b += `| | |\n|---|---|\n`;
  b += `| Seen | ${issue.count} times, ${issue.userCount ?? "?"} players |\n| First / last | ${issue.firstSeen} / ${issue.lastSeen} |\n`;
  b += `| Mod / game | ${tags.mod_version || "?"} / ${tags.game_version || "?"} |\n`;
  if (newestRelease && cmpVersion(tags.mod_version, newestRelease) < 0)
    b += `| Note | from an older version (newest release ${newestRelease}): check whether it's already fixed |\n`;
  if (tags.players) b += `| Players | ${tags.players}${tags.players !== "1" ? " (multiplayer)" : ""}: ${tags.characters || "?"} |\n`;
  if (tags.where) b += `| Where | \`${tags.where}\` |\n`;
  if (tags.audit) b += `| Audit | ${tags.audit} |\n`;
  b += `\n${kind === "mismatch" ? "A prediction didn't match what the game did." : "An exception was caught (the feature kept running or was turned off)."} Sections below are from the latest occurrence.\n`;
  b += block("Details", extra.details, 4000);
  b += block("Stack trace", stackTrace(event), 8000);
  b += block("Plays this turn (tabs → result)", extra.turn_plays, 4000);
  b += block("Simulator start state (JSON: replay it with tests/SpireQoL.Sim.Tests/Cases)", extra.sim_state, 12000);
  b += block("Simulator state after the play (JSON, read from the game)", extra.sim_state_after, 12000);
  b += block("Audit window (combat history during the check)", extra.audit_window, 5000);
  for (const k of ["player", "player_powers", "relics", "orbs", "enemies", "hand"]) b += block("before the play: " + k.replace("_", " "), extra["before_" + k], 5000);
  for (const k of ["combat", "player", "player_powers", "relics", "orbs", "enemies", "hand"]) b += block((k === "combat" ? "" : "after: ") + k.replace("_", " "), extra[k], 5000);
  b += block("Combat history (last 40 entries)", extra.combat_history, 5000);
  b += block("Recent mod log", extra.recent_log, 6000);
  return clip(b, 64000);
}

await ensureLabels();
await loadNewestRelease();
const mirrored = await mirroredIssues();
const issues = await sentry(`/projects/${ORG}/${PROJECT}/issues/?query=${encodeURIComponent("is:unresolved")}&statsPeriod=14d&limit=100`);
console.log(`${issues.length} unresolved Sentry issues, ${mirrored.size} mirrored.`);

let failed = 0;
for (const issue of issues) {
 try {
  const gh = mirrored.get(issue.id);
  if (!gh) {
    const event = await latestEvent(issue.id);
    if (!event) {
      console.log(`Sentry ${issue.id} has no event available yet; will retry next run.`);
      continue;
    }
    const kind = (event.tags || []).find((t) => t.key === "kind")?.value || "crash";
    const created = await github(`/repos/${REPO}/issues`, {
      method: "POST",
      body: JSON.stringify({ title: clip(`[${kind}] ${issue.title}`, 240), body: body(issue, event), labels: ["auto-report", kind] }),
    });
    console.log(`created #${created.number} for Sentry ${issue.id}: ${issue.title}`);
  } else if (gh.state === "closed") {
    const fix = await fixedIn(gh.number);
    if (new Date(issue.lastSeen) > new Date(gh.closed_at)) {
      // Again after the fix: from which versions?
      const again = await eventsSince(issue.id, gh.closed_at);
      const versions = [...new Set(again.map((e) => tagOf(e, "mod_version") || "?"))];
      const current = again.filter((e) => !fix || !(cmpVersion(tagOf(e, "mod_version"), fix) < 0));
      if (again.length > 0 && current.length === 0) {
        // Only players on versions older than the fix: nothing new to look at.
        await resolve(issue.id, fix);
        console.log(`#${gh.number} stays closed: seen again only from ${versions.join(", ")} (fixed in ${fix})`);
        continue;
      }
      const latest = current[0] ?? (await latestEvent(issue.id));
      const patch = { state: "open" };
      if (latest) patch.body = body(issue, latest);
      await github(`/repos/${REPO}/issues/${gh.number}`, { method: "PATCH", body: JSON.stringify(patch) });
      const from = current.length ? [...new Set(current.map((e) => tagOf(e, "mod_version") || "?"))].join(", ") : "unknown versions";
      await github(`/repos/${REPO}/issues/${gh.number}/comments`, {
        method: "POST",
        body: JSON.stringify({ body: `Happened again after this was closed${fix ? ` (fixed in ${fix})` : ""}, from ${from}: last seen ${issue.lastSeen} (${issue.count} times total). The report above is the newest occurrence. [Sentry](${issue.permalink})` }),
      });
      console.log(`reopened #${gh.number} (Sentry ${issue.id} seen again from ${from})`);
    } else {
      await resolve(issue.id, fix);
      console.log(`resolved Sentry ${issue.id} (#${gh.number} is closed${fix ? `, fixed in ${fix}` : ""})`);
    }
  }
 } catch (e) {
  // One issue failing doesn't stop the others from syncing; the run still fails at the end.
  failed++;
  console.error(`Sentry ${issue.id}: ${e.message}`);
 }
}
if (failed) {
  console.error(`${failed} issue(s) failed to sync.`);
  process.exit(1);
}
