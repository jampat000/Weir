#!/usr/bin/env node
// Release gate: the tag must equal the product version in both files that carry it, WeirVersion in
// apps/server/Directory.Build.props (the server, the tray, the Windows package and the Docker image) and
// apps/web/package.json.
//
// Usage (in release.yml): node scripts/check-release-version.mjs <tag, e.g. v3.2.4>
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const tag = (process.argv[2] || "").trim().replace(/^v/, "");
if (!tag) {
  console.error("Usage: node scripts/check-release-version.mjs <tag>");
  process.exit(2);
}

const props = readFileSync(path.join(repoRoot, "apps", "server", "Directory.Build.props"), "utf8");
const versions = [
  ["apps/server/Directory.Build.props WeirVersion", props.match(/<WeirVersion>([^<]+)<\/WeirVersion>/)?.[1]?.trim()],
  ["apps/web/package.json version", JSON.parse(readFileSync(path.join(repoRoot, "apps", "web", "package.json"), "utf8")).version],
];
const mismatches = versions.filter(([, value]) => value !== tag);
if (mismatches.length) {
  console.error(`::error::Release tag v${tag} does not match the versioned files:`);
  for (const [name, value] of mismatches) console.error(`  - ${name}: ${value}`);
  process.exit(1);
}
console.log(`Release versions aligned at ${tag}.`);
