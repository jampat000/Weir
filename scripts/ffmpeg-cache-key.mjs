#!/usr/bin/env node
// Prints the cache key for the Windows package's vendored FFmpeg: the checksum of the build
// packaging/windows/build-velopack.ps1 would download today. FFmpeg tracks BtbN's continuously republished
// `latest` build rather than a pinned version, so its checksum is the only thing that identifies what the
// cache holds: the cache is reused until upstream publishes a new build, and never saved once per run.
//
// Usage (a workflow step with an id): node scripts/ffmpeg-cache-key.mjs >> "$GITHUB_OUTPUT"   -> key=ffmpeg-vendor-<sha256>
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

const archiveName = assignment("ffmpegArchiveName");
const checksumsUrl = assignment("ffmpegChecksumsUrl");

const response = await fetch(checksumsUrl, { signal: AbortSignal.timeout(30_000) });
if (!response.ok) throw new Error(`GET ${checksumsUrl} returned ${response.status}.`);
const line = (await response.text())
  .split(/\r?\n/)
  .map((entry) => entry.trim().split(/\s+\*?/))
  .find(([, file]) => file === archiveName);
if (!line || !/^[0-9a-f]{64}$/i.test(line[0])) throw new Error(`No checksum for ${archiveName} in ${checksumsUrl}.`);

console.log(`key=ffmpeg-vendor-${line[0].toLowerCase()}`);
