#!/usr/bin/env node
// Fails when the contract suite's area list and its test classes disagree.
//
// CI runs one leg per name in apps/server/tests/Weir.Contract.Tests/areas.json with `--filter "Area=<name>"`.
// `dotnet test` passes when a filter matches nothing, so a misspelt or empty area would be a leg that always
// goes green. This check makes that impossible: every listed area has at least one [ContractArea("<name>")]
// class, and every area a class names is listed.
//
// Usage: node scripts/check-contract-areas.mjs
import { readdirSync, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const PROJECT = "apps/server/tests/Weir.Contract.Tests";
const ATTRIBUTE = /\[ContractArea\("([^"]+)"\)\]/g;

export function areasUsedIn(source) {
  return [...source.matchAll(ATTRIBUTE)].map((match) => match[1]);
}

export function compareAreas(listed, used) {
  const problems = [];
  const duplicates = listed.filter((name, index) => listed.indexOf(name) !== index);
  for (const name of new Set(duplicates)) problems.push(`areas.json lists "${name}" more than once.`);
  for (const name of listed) {
    if (!used.has(name)) problems.push(`areas.json lists "${name}", but no test class is marked [ContractArea("${name}")].`);
  }
  for (const [name, example] of used) {
    if (!listed.includes(name)) problems.push(`${example} is in area "${name}", which areas.json does not list, so CI would never run it.`);
  }
  return problems;
}

function sourceFiles(folder) {
  const files = [];
  for (const entry of readdirSync(folder, { withFileTypes: true })) {
    if (entry.name === "bin" || entry.name === "obj") continue;
    const full = path.join(folder, entry.name);
    if (entry.isDirectory()) files.push(...sourceFiles(full));
    else if (entry.name.endsWith(".cs")) files.push(full);
  }
  return files;
}

function main() {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
  const project = path.join(root, PROJECT);
  const listed = JSON.parse(readFileSync(path.join(project, "areas.json"), "utf8")).areas.map((area) => area.name);
  const used = new Map();
  for (const file of sourceFiles(project)) {
    const relative = path.relative(root, file).split(path.sep).join("/");
    for (const name of areasUsedIn(readFileSync(file, "utf8"))) {
      if (!used.has(name)) used.set(name, relative);
    }
  }
  const problems = compareAreas(listed, used);
  if (problems.length > 0) {
    console.error(problems.join("\n"));
    process.exit(1);
  }
  console.log(`Contract areas match their test classes: ${listed.join(", ")}.`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) main();
