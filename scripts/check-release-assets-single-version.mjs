#!/usr/bin/env node
// Release gate, defense in depth (#804): re-checks the Windows package output directory right before
// release.yml uploads it as the weir-windows-velopack artifact (and, from there, as GitHub Release
// assets), and fails the release outright if anything in it names a version other than the one being
// tagged. packaging/windows/build-velopack.ps1 already prunes this itself, right after `vpk pack`
// (scripts/prune-release-feed.mjs); this exists so a bug in that step fails loud here instead of
// silently re-shipping a stale package.
//
// Usage: node scripts/check-release-assets-single-version.mjs <dir> <X.Y.Z>

import { readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

import { ASSET_FEED_FILE, LEGACY_RELEASES_FILE, filterAssetFeed, filterLegacyReleases, nupkgsToRemove } from "./prune-release-feed.mjs";

export function findOtherVersionAssets(dir, version) {
  const problems = [];

  for (const name of nupkgsToRemove(readdirSync(dir), version)) {
    problems.push(`${name} is a package for a version other than ${version}.`);
  }

  const feedPath = path.join(dir, ASSET_FEED_FILE);
  const feedText = readFileOrNull(feedPath);
  if (feedText !== null) {
    const { dropped } = filterAssetFeed(feedText, version);
    for (const asset of dropped) problems.push(`${ASSET_FEED_FILE} lists ${asset.FileName} for version ${asset.Version}, not ${version}.`);
  }

  const legacyPath = path.join(dir, LEGACY_RELEASES_FILE);
  const legacyText = readFileOrNull(legacyPath);
  if (legacyText !== null) {
    const { dropped } = filterLegacyReleases(legacyText, version);
    for (const line of dropped) problems.push(`${LEGACY_RELEASES_FILE} has a line for another version: ${line}`);
  }

  return problems;
}

function readFileOrNull(filePath) {
  try {
    return readFileSync(filePath, "utf8");
  } catch (error) {
    if (error.code === "ENOENT") return null;
    throw error;
  }
}

function main() {
  const [dir, version] = process.argv.slice(2);
  if (!dir || !version) {
    console.error("Usage: node scripts/check-release-assets-single-version.mjs <dir> <X.Y.Z>");
    process.exit(2);
  }
  const problems = findOtherVersionAssets(dir, version);
  if (problems.length > 0) {
    console.error(`::error::${dir} would publish assets for a version other than ${version}:`);
    for (const problem of problems) console.error(`  - ${problem}`);
    process.exit(1);
  }
  console.log(`${dir} carries only version ${version}'s release assets.`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  main();
}
