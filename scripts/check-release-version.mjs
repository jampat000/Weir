#!/usr/bin/env node
// Release gate: the tag must be a well-formed SemVer release version, X.Y.Z or X.Y.Z-prerelease
// (for example 1.0.0-rc.1).
//
// #804: no file in the tree carries the release version. apps/server/Directory.Build.props'
// WeirVersion is a fixed placeholder (never bumped), and every build that ships stamps its own real
// version on the command line instead, taken from the tag: packaging/windows/build-velopack.ps1 passes
// it as WEIR_BUILD_VERSION -> -p:Version, the Dockerfile takes it as the WEIR_VERSION build-arg. Nothing
// else stops a malformed tag - "v3.2" or "v3.2.10.1" - from reaching those command lines and being baked
// into every shipped binary, image and package; release.yml's `v*` tag trigger is a glob, not a parser,
// so this is what actually validates the version before anything is built.
//
// A version with a prerelease part publishes as a GitHub pre-release (release.yml); build metadata
// ("+build") is refused because neither a Docker tag nor a package file name can carry it.
//
// Usage: node scripts/check-release-version.mjs <tag, e.g. v1.0.0-rc.1>
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

import { isPrerelease, parseSemver } from "./semver.mjs";

export function parseReleaseVersion(tag) {
  const trimmed = (tag ?? "").trim();
  const parsed = parseSemver(trimmed);
  if (!parsed) {
    return {
      ok: false,
      reason: `"${trimmed}" is not a release version in the form vX.Y.Z or vX.Y.Z-rc.1 (three non-negative integers without leading zeros, an optional prerelease part, no build suffix).`,
    };
  }
  return { ok: true, version: parsed.version, prerelease: isPrerelease(parsed) };
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
  const kind = result.prerelease ? "pre-release" : "release";
  console.log(`Release tag ${tag} is a well-formed ${kind} version (${result.version}).`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  main();
}
