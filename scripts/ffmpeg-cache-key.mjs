#!/usr/bin/env node
// Prints the cache key for the Windows package's vendored FFmpeg, read straight from the pin in
// packaging/windows/build-velopack.ps1: the release tag, the archive name and the committed
// archive SHA256. No network call is needed, unlike MKVToolNix's neighbouring pin further down
// this same file (whose cache key is a hash of the whole script instead, for the same reason).
// The cache changes exactly when the pin does, and never otherwise.
//
// Usage (a workflow step with an id): node scripts/ffmpeg-cache-key.mjs >> "$GITHUB_OUTPUT"   -> key=ffmpeg-vendor-<tag>-<archive>-<sha256>
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const buildScript = readFileSync(path.join(repoRoot, "packaging", "windows", "build-velopack.ps1"), "utf8");

// Read from the build script so the two can never disagree about which archive is meant.
function assignment(name) {
  const match = buildScript.match(new RegExp(`^\\$${name} = "([^"$]+)"`, "m"));
  if (!match) throw new Error(`$${name} was not found in build-velopack.ps1.`);
  return match[1];
}

const releaseTag = assignment("ffmpegReleaseTag");
const archiveName = assignment("ffmpegArchiveName");
const archiveSha256 = assignment("ffmpegArchiveSha256");
if (!/^[0-9a-f]{64}$/i.test(archiveSha256)) {
  throw new Error(`$ffmpegArchiveSha256 in build-velopack.ps1 is not a 64-character hex SHA256: ${archiveSha256}`);
}

console.log(`key=ffmpeg-vendor-${releaseTag}-${archiveName}-${archiveSha256.toLowerCase()}`);
