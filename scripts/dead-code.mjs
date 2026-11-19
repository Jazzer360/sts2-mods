// Lists members declared in the mods and core that nothing else references (by name), for a
// dead-code review. The compiler's analyzers catch unused private members (IDE0051/IDE0052); this
// catches public and internal ones. Names used by reflection, the game, JSON or interfaces show up
// too: check each before deleting. Then lists unused usings in every project (IDE0005, which the
// normal build doesn't check: implicit usings already give System, System.Linq, System.Threading...),
// from one build with that analyzer on; remove them with --fix-usings (dotnet format).
// Usage: node scripts/dead-code.mjs [--fix-usings] [--skip-usings]
import fs from "node:fs";
import path from "node:path";
import { execFileSync } from "node:child_process";

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
const external = new Set(["Id", "DisplayName", "EnabledByDefault", "Apply", "Attach", "OnFrame", "Main", "Initialize", "ToString", "Equals", "GetHashCode", "Dispose",
  // Read as string literals by the settings check (CatalogCheck reads ProcIndicatorsFeature's literals).
  "OutcomeTabs"]);
const decl = /^\s*(?:\[[^\]]*\]\s*)*(?:public|internal|protected|private)\s+(?:static\s+|readonly\s+|sealed\s+|partial\s+|abstract\s+|virtual\s+|const\s+|required\s+|override\s+|async\s+|unsafe\s+|new\s+)*(?:class|record|struct|enum|interface\s+)?[\w<>\[\],.?() ]*?\b([A-Z]\w*)\s*(?:\(|\{|=|;|<[\w, ]*>\s*\(|$)/gm;

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

// Unused usings (IDE0005): on only for this run (the .editorconfig line is put back as it was).
if (!process.argv.includes("--skip-usings")) {
  const projects = ["mods/SpireQoL/SpireQoL.csproj", "mods/MainSaves/MainSaves.csproj",
    "tests/SpireQoL.Sim.Tests/SpireQoL.Sim.Tests.csproj", "tests/SpireQoL.Catalog.Tests/SpireQoL.Catalog.Tests.csproj"];
  const fix = process.argv.includes("--fix-usings");
  const editorconfig = path.join(root, ".editorconfig");
  const original = fs.readFileSync(editorconfig, "utf8");
  const unused = new Set();
  try {
    fs.writeFileSync(editorconfig, original + "\ndotnet_diagnostic.IDE0005.severity = warning\n");
    for (const p of projects) {
      if (fix) {
        execFileSync("dotnet", ["format", "style", p, "--diagnostics", "IDE0005", "--severity", "warn"], { cwd: root, stdio: "inherit" });
        continue;
      }
      // IDE0005 only runs with code style enforced and XML docs generated; the doc warnings that brings are silenced.
      let out = "";
      try {
        out = execFileSync("dotnet", ["build", p, "-p:InstallToGame=false", "-p:EnforceCodeStyleInBuild=true", "-p:GenerateDocumentationFile=true",
          "-p:NoWarn=CS1591%3BCS1573%3BCS1572%3BCS1574%3BCS1584%3BCS1658%3BCS1570%3BCS1587%3BCS1734%3BCS1711%3BCS1712%3BCS1580%3BCS1581",
          "--no-incremental", "-v", "q"], { cwd: root, encoding: "utf8", maxBuffer: 64 * 1024 * 1024 });
      } catch (e) {
        out = String(e.stdout ?? "") + String(e.stderr ?? "");
      }
      for (const line of out.split("\n")) {
        const m = line.includes("IDE0005") && line.match(/sts2-mods[\\/](.+?)\((\d+),/);
        if (m) unused.add(`${m[1].replaceAll("\\", "/")}:${m[2]}`);
      }
    }
  } finally {
    fs.writeFileSync(editorconfig, original);
  }
  if (!fix) {
    const list = [...unused].sort();
    console.log(list.length ? `\nunused usings (node scripts/dead-code.mjs --fix-usings removes them):\n${list.join("\n")}` : "\nno unused usings");
  }
}
