#!/usr/bin/env node
// Installs the pinned static Linux FFmpeg (ffmpeg and ffprobe only) into a folder, so a CI job never takes it
// from a package mirror. The pin is the one the Windows package uses (scripts/ffmpeg-pin.mjs). The archive is
// downloaded with retries and its SHA-256 is checked before anything is unpacked; a folder that already holds
// both binaries (a cache hit, keyed on the pin) is left alone.
//
// Usage: node scripts/install-ffmpeg-linux.mjs <folder>
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { createReadStream, createWriteStream, existsSync, mkdirSync, mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { pipeline } from "node:stream/promises";
import { Readable } from "node:stream";
import { readFfmpegPin } from "./ffmpeg-pin.mjs";

const attempts = 4;
const attemptTimeoutMs = 5 * 60 * 1000;
const tools = ["ffmpeg", "ffprobe"];

async function download(url, file) {
  let lastError;
  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    try {
      const response = await fetch(url, { signal: AbortSignal.timeout(attemptTimeoutMs) });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      await pipeline(Readable.fromWeb(response.body), createWriteStream(file));
      return;
    } catch (error) {
      lastError = error;
      console.error(`Download attempt ${attempt} of ${attempts} failed: ${error.message}`);
      if (attempt < attempts) await new Promise((resolve) => setTimeout(resolve, attempt * 5000));
    }
  }
  throw new Error(`Could not download ${url}: ${lastError.message}`);
}

async function sha256Of(file) {
  const hash = createHash("sha256");
  await pipeline(createReadStream(file), hash);
  return hash.digest("hex");
}

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
  await download(linux.url, archive);

  const actual = await sha256Of(archive);
  if (actual !== linux.sha256) {
    throw new Error(`${linux.name} hash mismatch. Expected ${linux.sha256} but got ${actual}. Nothing was unpacked.`);
  }

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
