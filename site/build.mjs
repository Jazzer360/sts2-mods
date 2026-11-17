// Builds the Spire QoL site into site/dist: the feature tour (features.json + the captured screenshots
// in shots/), the install page with a direct download of the newest release, and the full changelog.
//
// The changelog and the feature reference are read from the main branch (raw.githubusercontent.com),
// and releases from GitHub's API, so every build is current without touching main.
// Node 18+, no dependencies:  node site/build.mjs
// Env: GITHUB_TOKEN (optional, raises the API rate limit), CHANGELOG_PATH / FEATURES_PATH (local files
// instead of main's), SITE_OFFLINE=1 (skip the network: no releases, local docs required).

import { readFile, writeFile, mkdir, cp, readdir } from "node:fs/promises";
import { existsSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { REPO, REPO_URL, RELEASES_API, cmpVersion, esc, parseChangelog, spireQolReleases, newestRelease, renderReleases, versionCount } from "./changelog.mjs";

const RAW = `https://raw.githubusercontent.com/${REPO}/main`;
const here = dirname(fileURLToPath(import.meta.url));
const dist = join(here, "dist");
const offline = process.env.SITE_OFFLINE === "1";
const warnings = [];
const warn = (msg) => { warnings.push(msg); console.warn(`::warning::${msg}`); };

// ---- inputs --------------------------------------------------------------------------------------

async function text(localEnv, path) {
  if (process.env[localEnv]) return readFile(process.env[localEnv], "utf8");
  if (offline) throw new Error(`${localEnv} is required with SITE_OFFLINE=1`);
  const res = await fetch(`${RAW}/${path}`);
  if (!res.ok) throw new Error(`${path}: HTTP ${res.status}`);
  return res.text();
}

async function releases() {
  if (offline) return [];
  const headers = { "User-Agent": "spireqol-site", Accept: "application/vnd.github+json" };
  if (process.env.GITHUB_TOKEN) headers.Authorization = `Bearer ${process.env.GITHUB_TOKEN}`;
  const all = [];
  for (let page = 1; page < 50; page++) {
    const res = await fetch(`${RELEASES_API}?per_page=100&page=${page}`, { headers });
    if (!res.ok) { warn(`releases: HTTP ${res.status}; building without release dates or download link`); return []; }
    const batch = await res.json();
    all.push(...batch);
    if (batch.length < 100) break;
  }
  return spireQolReleases(all);
}

// ---- pages ---------------------------------------------------------------------------------------

function layout({ title, page, body, latest }) {
  const nav = [["index.html", "Features"], ["install.html", "Install"], ["changelog.html", "Changelog"]]
    .map(([href, label]) => `<a href="${href}"${href === page ? ' aria-current="page"' : ""}>${label}</a>`).join("");
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${esc(title)}</title>
<meta name="description" content="Spire QoL: a quality-of-life mod for Slay the Spire 2 that tracks relic counters, card interactions, incoming damage and enemy moves for you, using only what a player could know.">
<link rel="stylesheet" href="style.css">
</head>
<body>
<header class="top">
  <a class="brand" href="index.html">Spire QoL</a>
  <nav>${nav}<a href="${REPO_URL}">GitHub</a></nav>
  <a class="btn small" data-download href="${latest?.zip ?? `${REPO_URL}/releases?q=spireqol`}">Download${latest ? ` ${esc(latest.version)}` : ""}</a>
</header>
<main>
${body}
</main>
<footer>
  <p>Spire QoL is a fan-made mod for Slay the Spire 2, not affiliated with Mega Crit. Screenshots are from the game with the mod installed.</p>
  <p><a href="${REPO_URL}">Source on GitHub</a> · <a href="${REPO_URL}/issues">Report a problem</a> · <a href="${REPO_URL}/blob/main/docs/FEATURES.md">Full feature reference</a></p>
</footer>
<div class="lightbox" hidden><img alt=""></div>
<script>
document.addEventListener("click", (e) => {
  const lb = document.querySelector(".lightbox");
  const img = e.target.closest("figure.shot img");
  if (img) { lb.querySelector("img").src = img.src; lb.querySelector("img").alt = img.alt; lb.hidden = false; return; }
  if (e.target.closest(".lightbox")) lb.hidden = true;
});
document.addEventListener("keydown", (e) => { if (e.key === "Escape") document.querySelector(".lightbox").hidden = true; });
// The page is rebuilt only when the site changes, so the download buttons ask GitHub for the newest release.
fetch("https://api.github.com/repos/${REPO}/releases?per_page=30").then((r) => r.ok ? r.json() : []).then((list) => {
  const num = (r) => r.tag_name.slice("spireqol-v".length).split(".").map(Number);
  const newer = (a, b) => { const x = num(a), y = num(b); for (let i = 0; i < 3; i++) if (x[i] !== y[i]) return x[i] > y[i]; return false; };
  // The API lists by creation date, not version: take the highest version.
  const rel = list.filter((r) => r.tag_name.startsWith("spireqol-v") && !r.draft && !r.prerelease && r.assets.some((a) => a.name.endsWith(".zip")))
    .reduce((best, r) => (!best || newer(r, best) ? r : best), null);
  if (!rel) return;
  const version = rel.tag_name.slice("spireqol-v".length);
  const zip = rel.assets.find((a) => a.name.endsWith(".zip")).browser_download_url;
  for (const a of document.querySelectorAll("a[data-download]")) { a.href = zip; a.textContent = (a.dataset.label || "Download") + " " + version; }
}).catch(() => {});
</script>
</body>
</html>
`;
}

function shotFigure(name, shots, haveFiles) {
  const s = shots.get(name);
  if (!haveFiles.has(`${name}.webp`)) {
    return `<figure class="shot missing"><div>Screenshot not captured yet</div></figure>`;
  }
  const dims = s ? ` width="${s.width}" height="${s.height}"` : "";
  // Narrow crops (a widget beside a character) are shown smaller, near their size in the game.
  const compact = s && s.frameWidth && s.width / s.frameWidth < 0.45;
  return `<figure class="shot${compact ? " compact" : ""}"><img src="shots/${name}.webp" alt="${esc(s?.caption || name)}" loading="lazy"${dims}>${s?.caption ? `<figcaption>${esc(s.caption)}</figcaption>` : ""}</figure>`;
}

function indexPage(features, shots, haveFiles, latest, manifest) {
  const sections = features.sections.map((sec) => `
<section class="group" id="${sec.id}">
  <h2>${esc(sec.title)}</h2>
  ${sec.features.map((f) => `
  <article class="feature" id="${f.id}">
    <div class="text">
      <h3>${esc(f.title)}</h3>
      <p>${esc(f.summary)}</p>
      ${f.points.length ? `<ul>${f.points.map((p) => `<li>${esc(p)}</li>`).join("")}</ul>` : ""}
    </div>
    <div class="shots">${f.shots.map((n) => shotFigure(n, shots, haveFiles)).join("")}</div>
  </article>`).join("")}
</section>`).join("");

  const toc = features.sections.map((sec) => `<li><a href="#${sec.id}">${esc(sec.title)}</a><ul>${sec.features.map((f) => `<li><a href="#${f.id}">${esc(f.title)}</a></li>`).join("")}</ul></li>`).join("");

  return layout({
    title: "Spire QoL: quality of life for Slay the Spire 2",
    page: "index.html",
    latest,
    body: `
<section class="hero">
  <div class="pitch">
    <h1>Quality of life for Slay the Spire 2.</h1>
    <p>Spire QoL keeps track of the things you'd otherwise have to hold in your head: relic counters, which of your powers and relics a card sets off, the damage coming your way after block, what an enemy does next, what your relics do with a reward, an event or a shop. So you can play fast and still make sharp decisions, without doing the math in your head.</p>
    <p>It does make the game easier. Not by breaking any rules or showing anything hidden, but by putting in front of you what an attentive player could already work out, and what a busy one would miss.</p>
    <p class="actions">
      <a class="btn" data-download href="${latest?.zip ?? `${REPO_URL}/releases?q=spireqol`}">Download${latest ? ` ${esc(latest.version)}` : ""}</a>
      <a class="btn ghost" href="install.html">How to install</a>
    </p>
    ${latest ? `<p class="meta">Released ${esc(latest.date)} · <a href="changelog.html">What's new</a></p>` : ""}
  </div>
  ${shotFigure("overview", shots, haveFiles)}
</section>

<section class="principles">
  <div><h3>Only what you could know</h3><p>It shows what a player with perfect memory and full knowledge of the rules could work out. It never reads the RNG or the seed, the draw pile's order, or an enemy's move beyond the intent on screen. Random effects show their amount or odds, never the roll.</p></div>
  <div><h3>The game's own rules</h3><p>Every prediction comes from one simulator that plays out your turn and the enemy turn with the game's damage, block and hook order. In-game checks compare each prediction with what really happened.</p></div>
  <div><h3>Display only</h3><p>It only reads the game and draws its own marks. Your cards, damage and saves are untouched, and it works in multiplayer lobbies whose other players don't have it.</p></div>
</section>

<nav class="toc"><h2>Features</h2><ul>${toc}</ul></nav>
${sections}
${manifest ? `<p class="meta center">Screenshots: Spire QoL ${esc(manifest.modVersion)} on Slay the Spire 2 ${esc(manifest.gameVersion)}, captured ${esc(manifest.captured)}.</p>` : ""}
`,
  });
}

function installPage(latest) {
  const dl = latest?.zip ?? `${REPO_URL}/releases?q=spireqol`;
  return layout({
    title: "Install Spire QoL",
    page: "install.html",
    latest,
    body: `
<article class="prose">
<h1>Install</h1>
<p class="actions"><a class="btn" data-download data-label="Download Spire QoL" href="${dl}">Download Spire QoL${latest ? ` ${esc(latest.version)}` : ""}</a>
${latest ? `<a class="btn ghost" href="${latest.url}">Release notes</a>` : ""}</p>
${latest ? `<p class="meta">Released ${esc(latest.date)}. Every release: <a href="${REPO_URL}/releases?q=spireqol">GitHub releases</a>.</p>` : ""}
<ol class="steps">
  <li><strong>Download</strong> the zip above (from a release page, take <code>SpireQoL-&lt;version&gt;.zip</code> under Assets, not "Source code").</li>
  <li><strong>Open your game folder.</strong> In Steam, right-click <em>Slay the Spire 2</em> → <em>Manage</em> → <em>Browse local files</em>.</li>
  <li><strong>Make a <code>mods</code> folder</strong> there if there isn't one already.</li>
  <li><strong>Extract the zip into <code>mods</code>.</strong> You should end up with:
<pre>Slay the Spire 2/
  mods/
    SpireQoL/
      SpireQoL.dll
      SpireQoL.json
      SpireQoL.pdb</pre>
  If you get <code>mods/SpireQoL/SpireQoL/…</code>, move the inner folder up a level.</li>
  <li><strong>Launch the game.</strong> The first time it finds a mod it asks whether to load mods: choose to load them. The game closes; start it again and the mod is active.</li>
</ol>

<h2>Using it</h2>
<ul>
  <li><strong>F10</strong> opens the settings menu (also a button on the pause menu and the Mods screen). Every feature, card tab and tracker row can be switched on or off there, and the <strong>Guide</strong> page explains every mark.</li>
  <li><strong>F8</strong> unlocks the layout in a fight: drag indicators, scroll to scale, right-click to reset. F8 again locks it.</li>
  <li><strong>F9</strong> hides or shows every overlay.</li>
</ul>

<h2>Your saves</h2>
<p>While any mod is installed, the game keeps a separate set of modded profiles: the first time you play with mods it copies your profiles, and from then on the two drift apart. To keep using your normal profiles, also install <strong>Main Saves</strong> the same way (<code>MainSaves-&lt;version&gt;.zip</code> from the <a href="${REPO_URL}/releases?q=mainsaves">releases page</a>, giving <code>mods/MainSaves/MainSaves.dll</code>). Delete <code>mods/MainSaves</code> to go back to the modded profiles, which are kept as they were.</p>

<h2>Updating and uninstalling</h2>
<p><strong>Update:</strong> extract the new zip over the old one, replacing the files. Your settings are kept. The main menu tells you when a newer version is out.</p>
<p><strong>Uninstall:</strong> delete the <code>mods/SpireQoL</code> folder.</p>

<h2>Multiplayer</h2>
<p>Spire QoL is display-only (<code>affects_gameplay: false</code>), so you can join lobbies whose other players don't have it.</p>

<h2>Problems</h2>
<p>The game's log is at <code>%APPDATA%\\SlayTheSpire2\\logs\\godot.log</code>; the mod's lines start with <code>[SpireQoL]</code>. Please include it when <a href="${REPO_URL}/issues">opening an issue</a>. Slay the Spire 2 is in early access and patches often, so a game update can break the mod until a new release is out.</p>

<h2>Error reporting</h2>
<p>When one of its features hits an error, or its own checks find that a prediction didn't match what the game did, Spire QoL sends a report: the mod and game versions, the error, and the fight at that moment (your character, HP, powers, relics, hand, the enemies and recent combat events). No personal information: no name, Steam ID, machine name or IP address. Each problem is sent at most once per session. Turn it off in the settings menu (General page) or with <code>"ReportErrors": false</code> in <code>mods/SpireQoL/SpireQoL.settings.cfg</code>.</p>
</article>`,
  });
}

function changelogPage(entries, released, latest) {
  // The release dates and links this copy was built with, for the live copy when GitHub's API can't be reached.
  const builtReleases = JSON.stringify(Object.fromEntries([...released].map(([v, r]) => [v, { date: r.date, url: r.url, zip: r.zip }])))
    .replace(/</g, "\\u003c");
  return layout({
    title: "Spire QoL changelog",
    page: "changelog.html",
    latest,
    body: `
<article class="prose changelog">
<h1>Changelog</h1>
<p id="count">${versionCount(entries.length)}</p>
<input type="search" id="filter" placeholder="Search the changelog (a card, relic, feature...)" aria-label="Search the changelog">
<div id="releases" data-versions="${entries.map((e) => e.version).join(",")}">${renderReleases(entries, released, latest?.version)}</div>
</article>
<script type="application/json" id="built-releases">${builtReleases}</script>
<script>
const box = document.getElementById("filter");
function applyFilter() {
  const q = box.value.trim().toLowerCase();
  for (const sec of document.querySelectorAll(".release")) {
    let any = !q;
    for (const li of sec.querySelectorAll("li")) {
      const hit = !q || li.textContent.toLowerCase().includes(q);
      li.style.display = hit ? "" : "none";
      if (hit) any = true;
    }
    sec.style.display = any ? "" : "none";
  }
}
box.addEventListener("input", applyFilter);
</script>
<script type="module">
// The copy above was built with the site. Load CHANGELOG.md from main and show that instead, so the page is
// current without a rebuild; keep the built copy when the fetch fails, or when the fetched file is older
// (raw.githubusercontent.com can serve a stale copy for a few minutes).
import { RAW_CHANGELOG, RELEASES_API, parseChangelog, spireQolReleases, newestRelease, renderReleases, versionCount } from "./changelog.mjs";
const holder = document.getElementById("releases");
const built = holder.dataset.versions.split(",").filter(Boolean);
async function releaseMap() {
  const map = new Map(Object.entries(JSON.parse(document.getElementById("built-releases").textContent)));
  try {
    const items = [];
    for (let page = 1; page <= 5; page++) {
      const res = await fetch(\`\${RELEASES_API}?per_page=100&page=\${page}\`);
      if (!res.ok) return map;
      const batch = await res.json();
      items.push(...batch);
      if (batch.length < 100) break;
    }
    for (const r of spireQolReleases(items)) map.set(r.version, r);
  } catch {}
  return map;
}
try {
  const [res, released] = await Promise.all([fetch(RAW_CHANGELOG, { cache: "no-cache" }), releaseMap()]);
  if (res.ok) {
    const entries = parseChangelog(await res.text());
    const have = new Set(entries.map((e) => e.version));
    if (entries.length >= built.length && built.every((v) => have.has(v))) {
      const latest = newestRelease([...released].map(([version, r]) => ({ version, ...r })));
      const anchor = location.hash;
      holder.innerHTML = renderReleases(entries, released, latest?.version);
      holder.dataset.versions = entries.map((e) => e.version).join(",");
      document.getElementById("count").textContent = versionCount(entries.length);
      applyFilter();
      if (anchor) document.getElementById(anchor.slice(1))?.scrollIntoView();
    }
  }
} catch {}
</script>`,
  });
}

// ---- coverage: every feature in the reference has a place on the site ------------------------------

function checkCoverage(features, featuresMd) {
  const headings = [...featuresMd.matchAll(/^## (.+)$/gm)].map((m) => m[1].trim());
  const covered = new Set(features.sections.flatMap((s) => s.features.flatMap((f) => f.doc)));
  for (const ignored of features.ignoreDocHeadings) covered.add(ignored);
  for (const h of headings) if (!covered.has(h)) warn(`docs/FEATURES.md has "## ${h}", which no feature in site/features.json covers`);
  for (const h of covered) if (!headings.includes(h)) warn(`site/features.json names "${h}", which docs/FEATURES.md no longer has`);
}

// ---- main ----------------------------------------------------------------------------------------

const features = JSON.parse(await readFile(join(here, "features.json"), "utf8"));
const shotsDir = join(here, "shots");
const manifest = existsSync(join(shotsDir, "shots.json")) ? JSON.parse((await readFile(join(shotsDir, "shots.json"), "utf8")).replace(/^﻿/, "")) : null;
const shots = new Map((manifest?.shots ?? []).map((s) => [s.name, s]));
const haveFiles = new Set(existsSync(shotsDir) ? await readdir(shotsDir) : []);
if (!haveFiles.has("overview.webp")) warn(`no screenshot for "overview" (the front page) yet`);
for (const f of features.sections.flatMap((s) => s.features)) for (const n of f.shots) if (!haveFiles.has(`${n}.webp`)) warn(`no screenshot for "${n}" (${f.title}) yet`);

const [changelogMd, featuresMd, rels] = await Promise.all([text("CHANGELOG_PATH", "CHANGELOG.md"), text("FEATURES_PATH", "docs/FEATURES.md"), releases()]);
checkCoverage(features, featuresMd);
const released = new Map(rels.map((r) => [r.version, r]));
const latest = newestRelease(rels);
const entries = parseChangelog(changelogMd);

await mkdir(dist, { recursive: true });
await writeFile(join(dist, "index.html"), indexPage(features, shots, haveFiles, latest, manifest));
await writeFile(join(dist, "install.html"), installPage(latest));
await writeFile(join(dist, "changelog.html"), changelogPage(entries, released, latest));
await cp(join(here, "style.css"), join(dist, "style.css"));
await cp(join(here, "changelog.mjs"), join(dist, "changelog.mjs"));
if (existsSync(shotsDir)) await cp(shotsDir, join(dist, "shots"), { recursive: true });
await writeFile(join(dist, ".nojekyll"), "");

console.log(`Built ${dist}: ${entries.length} changelog versions, latest release ${latest?.version ?? "(none found)"}, ${haveFiles.size} shot files, ${warnings.length} warnings.`);
