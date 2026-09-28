#!/usr/bin/env node
// Release gate: the tag must be a well-formed release version, X.Y.Z.
//
// #804: no file in the tree carries the release version any more. apps/server/Directory.Build.props'
// WeirVersion is a fixed placeholder (never bumped), and every build that ships stamps its own real
// version on the command line instead, taken from the tag: packaging/windows/build-velopack.ps1 passes
// it as WEIR_BUILD_VERSION -> -p:Version, the Dockerfile takes it as the WEIR_VERSION build-arg. That
// removed the version-bump PR (and the checked-in-version-vs-tag check this script used to do), but it
// also means nothing stops a malformed tag - "v3.2" or "v3.2.10.1" - from reaching those command lines
// and being baked into every shipped binary, image and package. release.yml's own tag trigger
// (`v*` / `!v*-*`) already excludes prerelease suffixes, but a glob is not a parser; this is what
// actually validates the version before anything is built.
//
// Usage: node scripts/check-release-version.mjs <tag, e.g. v3.2.4>
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

export function parseReleaseVersion(tag) {
  const trimmed = (tag ?? "").trim();
  const withoutV = trimmed.startsWith("v") ? trimmed.slice(1) : trimmed;
  const match = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.exec(withoutV);
  if (!match) {
    return { ok: false, reason: `"${trimmed}" is not a release version in the form vX.Y.Z (three non-negative integers, no leading zeros, no prerelease or build suffix).` };
  }
  return { ok: true, version: withoutV };
}

function main() {
  const tag = process.argv[2] || "";
  if (!tag) {
    console.error("Usage: node scripts/check-release-version.mjs <tag>");
    process.exit(2);
  }
  const result = parseReleaseVersion(tag);
  if (!result.ok) {
    console.error(`::error::${result.reason}`);
    process.exit(1);
  }
  console.log(`Release tag ${tag} is a well-formed release version (${result.version}).`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  main();
}
