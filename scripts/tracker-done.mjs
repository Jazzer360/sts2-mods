// Moves the tracker's done items out of docs/TRACKER.md into docs/tracker-done.md, under the same section headings, so the tracker holds
// what's open (to do, built but unchecked) and the record of what was done stays a link away. A done item is a top-level "- [x]" line with
// the indented lines under it; "[ ]" and "[~]" items, notes and the known-gaps block stay. Run it whenever the done items pile up:
//   node scripts/tracker-done.mjs
import fs from "node:fs";
import path from "node:path";

const root = path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1")), "..");
const trackerPath = path.join(root, "docs", "TRACKER.md");
const donePath = path.join(root, "docs", "tracker-done.md");

const lines = fs.readFileSync(trackerPath, "utf8").split(/\r?\n/);
const kept = [];
const moved = new Map(); // section heading (## or ###, joined) -> lines
const order = [];
let section = "", subsection = "";
let moving = false;
for (const line of lines) {
  if (/^## /.test(line)) { section = line; subsection = ""; moving = false; kept.push(line); continue; }
  if (/^### /.test(line)) { subsection = line; moving = false; kept.push(line); continue; }
  if (/^- \[x\]/.test(line)) moving = true;
  else if (!/^\s+\S/.test(line)) moving = false; // anything not indented ends the item (a blank line, another item, a note)
  if (!moving) { kept.push(line); continue; }
  const key = subsection ? `${section}\n${subsection}` : section;
  if (!moved.has(key)) { moved.set(key, []); order.push(key); }
  moved.get(key).push(line);
}
const count = [...moved.values()].reduce((sum, list) => sum + list.filter(line => /^- \[x\]/.test(line)).length, 0);
if (count === 0) { console.log("no done items to move"); process.exit(0); }

// Append under each heading in tracker-done.md (created the first time), the newest moves last within a section.
let done = fs.existsSync(donePath) ? fs.readFileSync(donePath, "utf8").replace(/\r\n/g, "\n").split("\n") : [
  "# Tracker: done",
  "",
  "The tracker's finished items, moved out of [TRACKER.md](TRACKER.md) by `node scripts/tracker-done.mjs` under the section they were in, as they were",
  "written when checked off: the record of how each was done. What's open stays in the tracker.",
  "",
];
for (const key of order) {
  const headings = key.split("\n");
  let at = -1;
  for (let i = 0, want = 0; i < done.length && want < headings.length; i++)
    if (done[i] === headings[want]) { at = i; want++; if (want < headings.length) at = -1; }
  if (at < 0) {
    if (done[done.length - 1] !== "") done.push("");
    done.push(...headings, "");
    at = done.length - 1;
    done.splice(at, 0, ...moved.get(key));
  } else {
    // After the heading's last item (the next heading or the end).
    let end = at + 1;
    while (end < done.length && !/^#{2,3} /.test(done[end])) end++;
    while (end > at + 1 && done[end - 1] === "") end--;
    done.splice(end, 0, ...moved.get(key));
  }
}
// A section left with only its heading and blank lines in the tracker points to where its done items went.
const out = [];
for (let i = 0; i < kept.length; i++) {
  out.push(kept[i]);
  if (/^#{2,3} /.test(kept[i]) && moved.has(kept[i]) || [...moved.keys()].some(key => key.endsWith("\n" + kept[i]) || key === kept[i])) {
    const next = kept.slice(i + 1).find(line => line.trim() !== "");
    // A section is empty when nothing but the next section (or a rule) follows it; a subsection, when any heading does.
    const ends = /^## /.test(kept[i]) ? /^## |^---$/ : /^#{2,3} |^---$/;
    if (next === undefined || ends.test(next)) out.push("", "Everything here is done: [tracker-done.md](tracker-done.md).");
  }
}
fs.writeFileSync(trackerPath, out.join("\n").replace(/\n{3,}/g, "\n\n"));
fs.writeFileSync(donePath, done.join("\n").replace(/\n{3,}/g, "\n\n").replace(/\n*$/, "\n"));
console.log(`moved ${count} done items to docs/tracker-done.md`);
