#!/usr/bin/env node
// Prints the cache key for a vendored FFmpeg, read straight from the pin in
// packaging/windows/build-velopack-vendored-media-tools.ps1 (dot-sourced from build-velopack.ps1):
// the release tag, the archive name and the committed archive SHA256. No network call is needed,
// unlike MKVToolNix's neighbouring pin further down that same file (whose cache key is a hash of
// the whole file instead, for the same reason). The cache changes exactly when the pin does, and
// never otherwise.
//
// Usage (a workflow step with an id):
//   node scripts/ffmpeg-cache-key.mjs >> "$GITHUB_OUTPUT"         -> key=ffmpeg-vendor-<tag>-<windows archive>-<sha256>
//   node scripts/ffmpeg-cache-key.mjs linux >> "$GITHUB_OUTPUT"   -> key=ffmpeg-linux-<tag>-<linux archive>-<sha256>
import { readFfmpegPin } from "./ffmpeg-pin.mjs";

const platform = process.argv[2] ?? "windows";
if (platform !== "windows" && platform !== "linux") {
  console.error(`Usage: node scripts/ffmpeg-cache-key.mjs [windows|linux] (got "${platform}")`);
  process.exit(2);
}

const pin = readFfmpegPin();
const archive = pin[platform];
const prefix = platform === "windows" ? "ffmpeg-vendor" : "ffmpeg-linux";
console.log(`key=${prefix}-${pin.releaseTag}-${archive.name}-${archive.sha256}`);
