// Mirrors the mod's Sentry issues to GitHub issues (run by .github/workflows/sentry-issues.yml).
//
//  - New unresolved Sentry issue        → new GitHub issue labelled auto-report + crash/mismatch,
//                                         with the full report (stack trace, game state, logs).
//  - GitHub issue closed (fixed)        → Sentry issue resolved.
//  - Happened again after being closed  → GitHub issue reopened with a comment.
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
  if (tags.where) b += `| Where | \`${tags.where}\` |\n`;
  if (tags.audit) b += `| Audit | ${tags.audit} |\n`;
  b += `\n${kind === "mismatch" ? "A prediction didn't match what the game did." : "An exception was caught (the feature kept running or was turned off)."} Sections below are from the latest occurrence.\n`;
  b += block("Details", extra.details, 4000);
  b += block("Stack trace", stackTrace(event), 8000);
  b += block("Audit window (combat history during the check)", extra.audit_window, 5000);
  for (const k of ["combat", "player", "player_powers", "relics", "orbs", "enemies", "hand"]) b += block(k.replace("_", " "), extra[k], 5000);
  b += block("Combat history (last 40 entries)", extra.combat_history, 5000);
  b += block("Recent mod log", extra.recent_log, 6000);
  return clip(b, 64000);
}

await ensureLabels();
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
    if (new Date(issue.lastSeen) > new Date(gh.closed_at)) {
      await github(`/repos/${REPO}/issues/${gh.number}`, { method: "PATCH", body: JSON.stringify({ state: "open" }) });
      await github(`/repos/${REPO}/issues/${gh.number}/comments`, {
        method: "POST",
        body: JSON.stringify({ body: `Happened again after this was closed: last seen ${issue.lastSeen} (${issue.count} times total). [Sentry](${issue.permalink})` }),
      });
      console.log(`reopened #${gh.number} (Sentry ${issue.id} regressed)`);
    } else {
      await sentry(`/organizations/${ORG}/issues/${issue.id}/`, { method: "PUT", body: JSON.stringify({ status: "resolved" }) });
      console.log(`resolved Sentry ${issue.id} (#${gh.number} is closed)`);
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
