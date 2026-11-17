// The changelog's parser and markup, shared by build.mjs (the copy built into changelog.html) and the
// changelog page itself, which loads CHANGELOG.md from main in the browser and renders it the same way.
// No dependencies, runs in Node and in browsers.

export const REPO = "Jazzer360/sts2-mods";
export const REPO_URL = `https://github.com/${REPO}`;
export const RAW_CHANGELOG = `https://raw.githubusercontent.com/${REPO}/main/CHANGELOG.md`;
export const RELEASES_API = `https://api.github.com/repos/${REPO}/releases`;

export const cmpVersion = (a, b) => {
  const pa = a.split(".").map(Number), pb = b.split(".").map(Number);
  for (let i = 0; i < 3; i++) if ((pa[i] ?? 0) !== (pb[i] ?? 0)) return (pa[i] ?? 0) - (pb[i] ?? 0);
  return 0;
};

// ---- a small Markdown subset (what CHANGELOG.md uses) ---------------------------------------------

export const esc = (s) => String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");

export function inline(s) {
  const codes = [];
  s = s.replace(/`([^`]+)`/g, (_, c) => `\u0000${codes.push(c) - 1}\u0000`);
  s = esc(s);
  s = s.replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, (_, label, href) => {
    if (!/^https?:/.test(href)) href = `${REPO_URL}/blob/main/${href.replace(/^\.?\//, "")}`;
    return `<a href="${href}">${label}</a>`;
  });
  s = s.replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>").replace(/(^|[^*])\*([^*\s][^*]*)\*/g, "$1<em>$2</em>");
  return s.replace(/\u0000(\d+)\u0000/g, (_, i) => `<code>${esc(codes[i])}</code>`);
}

export function markdownList(lines) {
  // Nested "- item" lists by indent; continuation lines join the item above.
  let html = "", depth = -1;
  for (const raw of lines) {
    const m = raw.match(/^(\s*)[-*] (.*)$/);
    if (!m) {
      if (html.endsWith("</li>")) html = html.slice(0, -5) + " " + inline(raw.trim()) + "</li>";
      else html += `<p>${inline(raw.trim())}</p>`;
      continue;
    }
    const indent = Math.floor(m[1].length / 2);
    while (depth < indent) { html += "<ul>"; depth++; }
    while (depth > indent) { html += "</ul></li>"; depth--; }
    html += `<li>${inline(m[2])}</li>`;
  }
  while (depth-- > 0) html += "</ul></li>";
  if (html.includes("<ul>")) html += "</ul>";
  // A nested list opens inside the item above it.
  return html.replace(/<\/li><ul>/g, "<ul>");
}

// ---- changelog -----------------------------------------------------------------------------------

/** Every "### Spire QoL x.y.z" section of CHANGELOG.md, newest version first. */
export function parseChangelog(md) {
  const entries = [];
  let cur = null, kind = null;
  for (const line of md.split(/\r?\n/)) {
    const v = line.match(/^### Spire QoL (\d+\.\d+\.\d+)/);
    if (v) { cur = { version: v[1], groups: [] }; entries.push(cur); kind = null; continue; }
    if (/^###? /.test(line)) { cur = null; continue; }
    if (!cur) continue;
    const k = line.match(/^#### (.+)$/);
    if (k) { kind = { name: k[1].trim(), lines: [] }; cur.groups.push(kind); continue; }
    if (!line.trim()) continue;
    if (!kind) { kind = { name: "", lines: [] }; cur.groups.push(kind); }
    kind.lines.push(line);
  }
  return entries.sort((a, b) => cmpVersion(b.version, a.version));
}

/** Spire QoL's releases from the GitHub releases API's items: version, date, page and zip. */
export function spireQolReleases(apiItems) {
  return apiItems
    .filter((r) => r.tag_name.startsWith("spireqol-v") && !r.draft && !r.prerelease)
    .map((r) => ({
      version: r.tag_name.slice("spireqol-v".length),
      date: (r.published_at || r.created_at || "").slice(0, 10),
      url: r.html_url,
      zip: (r.assets || []).find((a) => a.name.endsWith(".zip"))?.browser_download_url ?? null,
    }));
}

/** The newest release with a zip, or null. */
export const newestRelease = (releases) =>
  releases.filter((r) => r.zip).sort((a, b) => cmpVersion(b.version, a.version))[0] ?? null;

/** The changelog's versions as markup. `released` maps a version to its release ({ date, url }). */
export function renderReleases(entries, released, latestVersion) {
  return entries.map((e) => {
    const rel = released.get(e.version);
    // Early versions predate GitHub releases; only a version past the newest release is "unreleased".
    const pending = !rel && latestVersion && cmpVersion(e.version, latestVersion) > 0;
    const date = rel ? `<time>${esc(rel.date)}</time>` : pending ? `<span class="tag">unreleased</span>` : "";
    const groups = e.groups.map((g) => `${g.name ? `<h4 class="kind ${esc(g.name.toLowerCase())}">${esc(g.name)}</h4>` : ""}${markdownList(g.lines)}`).join("");
    return `<section class="release" id="v${e.version}"><h2><a href="#v${e.version}">${esc(e.version)}</a> ${date}${rel ? ` <a class="small" href="${esc(rel.url)}">release</a>` : ""}</h2>${groups}</section>`;
  }).join("");
}

export const versionCount = (n) => `Every Spire QoL release, newest first (${n} versions).`;
