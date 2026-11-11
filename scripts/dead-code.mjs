// Lists members declared in the mods and core that nothing else references (by name), for a
// dead-code review. The compiler's analyzers catch unused private members (IDE0051/IDE0052); this
// catches public and internal ones. Names used by reflection, the game, JSON or interfaces show up
// too: check each before deleting. Usage: node scripts/dead-code.mjs
import fs from "node:fs";
import path from "node:path";

const root = path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1")), "..");
const files = [];
(function walk(d) {
  for (const f of fs.readdirSync(d, { withFileTypes: true })) {
    const p = path.join(d, f.name);
    if (f.isDirectory()) { if (!["bin", "obj", "dist", ".git", "node_modules"].includes(f.name)) walk(p); }
    else if (f.name.endsWith(".cs")) files.push(p);
  }
})(root);
const src = Object.fromEntries(files.map(f => [f, fs.readFileSync(f, "utf8")]));
const all = Object.values(src).join("\n");

// Interface members, overrides and entry points the game or the framework calls.
const external = new Set(["Id", "DisplayName", "EnabledByDefault", "Apply", "Attach", "OnFrame", "Main", "Initialize", "ToString", "Equals", "GetHashCode", "Dispose"]);
const decl = /^\s*(?:\[[^\]]*\]\s*)*(?:public|internal|protected|private)\s+(?:static\s+|readonly\s+|sealed\s+|partial\s+|abstract\s+|virtual\s+|const\s+|required\s+|override\s+|async\s+|unsafe\s+|new\s+)*(?:class|record|struct|enum|interface\s+)?[\w<>\[\],.?() ]*?\b([A-Z]\w*)\s*(?:\(|\{|=|;|<|$)/gm;

const findings = [];
for (const [f, s] of Object.entries(src)) {
  if (f.includes(`${path.sep}tests${path.sep}`)) continue;
  for (const m of s.matchAll(decl)) {
    const line = m[0];
    if (/\boverride\b/.test(line)) continue;
    const name = m[1];
    if (external.has(name)) continue;
    const uses = (all.match(new RegExp(`\\b${name}\\b`, "g")) || []).length;
    if (uses <= 1) findings.push(`${path.relative(root, f)}: ${name}`);
  }
}
console.log(findings.length ? findings.join("\n") : "nothing unreferenced");
