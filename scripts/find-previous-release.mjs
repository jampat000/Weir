#!/usr/bin/env node
// The release the Windows package's delta is built against: the newest published release that is
// older than the one being released, by SemVer precedence. A release that is not older (a newer
// version that is still published, or the same one re-run) is never the base, and when nothing is
// older the release is a full package only, which every install can always fall back to.
//
// release.yml feeds it the tags of the repository's published releases, one per line, and reads the bare
// version it prints (nothing when there is no base).
//
// Usage: gh api --paginate repos/<owner>/<repo>/releases --jq '.[] | select(.draft | not) | .tag_name' \
//          | node scripts/find-previous-release.mjs <version being released>
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

import { compareSemver, parseSemver } from "./semver.mjs";

/** The newest of `tags` that is older than `releasing`; null when there is none. Tags that are not versions are ignored. */
export function findPreviousVersion(tags, releasing) {
  const current = parseSemver(releasing);
  if (!current) throw new Error(`"${releasing}" is not a release version.`);

  let previous = null;
  for (const tag of tags) {
    const candidate = parseSemver(tag);
    if (!candidate || compareSemver(candidate, current) >= 0) continue;
    if (previous === null || compareSemver(candidate, previous) > 0) previous = candidate;
  }
  return previous?.version ?? null;
}

function main() {
  const releasing = process.argv[2] || "";
  if (!releasing) {
    console.error("Usage: node scripts/find-previous-release.mjs <version being released>  (release tags on stdin, one per line)");
    process.exit(2);
  }
  const tags = readFileSync(0, "utf8").split(/\r?\n/).filter((line) => line.trim().length > 0);
  console.log(findPreviousVersion(tags, releasing) ?? "");
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  main();
}
