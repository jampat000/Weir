#!/usr/bin/env node
// After `vpk pack` (packaging/windows/build-velopack.ps1), the Velopack output directory still holds
// the previous release's full nupkg - fetched so vpk could build a delta against it, see docs/release.md
// - and both the releases.win.json feed and the legacy RELEASES feed list it alongside the version
// actually being released. Publishing it would re-ship a package nobody asked for and make every
// release's assets grow release over release (#804). Per Velopack's own docs ("any one GitHub release
// must only have 1 full package and 1 delta update to facilitate proper updating" -
// https://docs.velopack.io, Delta Updates): a client already on the previous version has that version's
// full package cached locally from when it installed or last updated, and only needs this release's own
// delta; a client further behind chains deltas across several past releases' own feed entries instead.
// This prunes the output directory back down to exactly the version being released: its own packages,
// and a feed that names only them. scripts/check-release-assets-single-version.mjs re-checks the result
// right before release.yml uploads it, as a defense-in-depth backstop for this script.
//
// Usage: node scripts/prune-release-feed.mjs --output-dir <dir> --version <X.Y.Z>

import { readFileSync, readdirSync, unlinkSync, writeFileSync } from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

export const ASSET_FEED_FILE = "releases.win.json";
export const LEGACY_RELEASES_FILE = "RELEASES";
const BOM = "﻿";

/** The packages in an output directory listing that do not belong to `version`. */
export function nupkgsToRemove(fileNames, version) {
  const marker = `-${version}-`;
  return fileNames.filter((name) => name.toLowerCase().endsWith(".nupkg") && !name.includes(marker));
}

/** releases.win.json: `{ Assets: [{ PackageId, Version, Type, FileName, SHA1, SHA256, Size, ... }] }`. */
export function filterAssetFeed(feedJson, version) {
  const feed = JSON.parse(feedJson);
  const assets = Array.isArray(feed.Assets) ? feed.Assets : [];
  const kept = assets.filter((asset) => asset.Version === version);
  const dropped = assets.filter((asset) => asset.Version !== version);
  return { json: JSON.stringify({ ...feed, Assets: kept }), kept, dropped };
}

/**
 * The legacy Squirrel-compatible feed: one "<SHA1> <filename> <size>" line per package, UTF-8 with a
 * leading BOM, no trailing newline. It carries no version field of its own, so entries are matched by
 * filename the same way `nupkgsToRemove` matches files on disk.
 */
export function filterLegacyReleases(text, version) {
  const withoutBom = text.startsWith(BOM) ? text.slice(BOM.length) : text;
  const marker = `-${version}-`;
  const lines = withoutBom.split("\n").filter((line) => line.trim().length > 0);
  const kept = lines.filter((line) => line.includes(marker));
  const dropped = lines.filter((line) => !line.includes(marker));
  return { text: BOM + kept.join("\n"), kept, dropped };
}

export function prune(outputDir, version) {
  const removedFiles = [];
  for (const name of nupkgsToRemove(readdirSync(outputDir), version)) {
    unlinkSync(path.join(outputDir, name));
    removedFiles.push(name);
  }

  const droppedAssets = rewriteIfPresent(path.join(outputDir, ASSET_FEED_FILE), (contents) => {
    const { json, dropped } = filterAssetFeed(contents, version);
    return { rewritten: json, dropped };
  });

  const droppedLines = rewriteIfPresent(path.join(outputDir, LEGACY_RELEASES_FILE), (contents) => {
    const { text, dropped } = filterLegacyReleases(contents, version);
    return { rewritten: text, dropped };
  });

  return { removedFiles, droppedAssets, droppedLines };
}

function rewriteIfPresent(filePath, filter) {
  let contents;
  try {
    contents = readFileSync(filePath, "utf8");
  } catch (error) {
    if (error.code === "ENOENT") return [];
    throw error;
  }
  const { rewritten, dropped } = filter(contents);
  if (dropped.length > 0) writeFileSync(filePath, rewritten, "utf8");
  return dropped;
}

function valueOf(args, flag) {
  const index = args.indexOf(flag);
  return index >= 0 ? args[index + 1] : undefined;
}

function main() {
  const args = process.argv.slice(2);
  const outputDir = valueOf(args, "--output-dir");
  const version = valueOf(args, "--version");
  if (!outputDir || !version) {
    console.error("Usage: node scripts/prune-release-feed.mjs --output-dir <dir> --version <X.Y.Z>");
    process.exit(2);
  }
  const { removedFiles, droppedAssets, droppedLines } = prune(outputDir, version);
  if (removedFiles.length === 0 && droppedAssets.length === 0 && droppedLines.length === 0) {
    console.log(`No other-version release assets found in ${outputDir}; nothing to prune.`);
    return;
  }
  for (const name of removedFiles) console.log(`Removed other-version package: ${name}`);
  for (const asset of droppedAssets) console.log(`Dropped ${ASSET_FEED_FILE} entry: ${asset.FileName} (${asset.Version})`);
  for (const line of droppedLines) console.log(`Dropped ${LEGACY_RELEASES_FILE} line: ${line}`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  main();
}
