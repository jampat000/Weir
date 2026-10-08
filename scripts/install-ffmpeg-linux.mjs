#!/usr/bin/env node
// Installs the pinned static Linux FFmpeg (ffmpeg and ffprobe only) into a folder, so a CI job never takes it
// from a package mirror. The pin is the one the Windows package uses (scripts/ffmpeg-pin.mjs). The archive is
// downloaded with retries (from Weir's own releases when BtbN no longer has it, scripts/pinned-download.mjs) and
// its SHA-256 is checked before anything is unpacked; a folder that already holds both binaries (a cache hit,
// keyed on the pin) is left alone.
//
// Usage: node scripts/install-ffmpeg-linux.mjs <folder>
import { spawnSync } from "node:child_process";
import { existsSync, mkdirSync, mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { readFfmpegPin } from "./ffmpeg-pin.mjs";
import { downloadPinned } from "./pinned-download.mjs";

const tools = ["ffmpeg", "ffprobe"];

const destination = process.argv[2];
if (!destination) {
  console.error("Usage: node scripts/install-ffmpeg-linux.mjs <folder>");
  process.exit(2);
}

if (tools.every((tool) => existsSync(path.join(destination, tool)))) {
  console.log(`ffmpeg and ffprobe are already in ${destination}.`);
  process.exit(0);
}

const { linux } = readFfmpegPin();
const scratch = mkdtempSync(path.join(tmpdir(), "weir-ffmpeg-"));
try {
  const archive = path.join(scratch, linux.name);
  console.log(`Downloading ${linux.url}`);
  await downloadPinned({ url: linux.url, fileName: linux.name, file: archive, sha256: linux.sha256 });

  mkdirSync(destination, { recursive: true });
  const unpacked = spawnSync(
    "tar",
    ["-xJf", archive, "-C", destination, "--strip-components=2", "--wildcards", ...tools.map((tool) => `*/bin/${tool}`)],
    { stdio: "inherit" },
  );
  if (unpacked.status !== 0) throw new Error(`Unpacking ${linux.name} failed.`);
  console.log(`Installed ${tools.join(" and ")} into ${destination}.`);
} finally {
  rmSync(scratch, { recursive: true, force: true });
}
