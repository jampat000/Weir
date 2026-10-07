// node --test scripts/ffmpeg-pin.test.mjs
import assert from "node:assert/strict";
import { test } from "node:test";

import { readFfmpegPin } from "./ffmpeg-pin.mjs";

test("the committed pin names a Windows and a Linux archive of one release, each with a SHA-256", () => {
  const pin = readFfmpegPin();
  assert.match(pin.windows.name, /-win64-.*\.zip$/);
  assert.match(pin.linux.name, /-linux64-lgpl-.*\.tar\.xz$/);
  for (const archive of [pin.windows, pin.linux]) {
    assert.match(archive.sha256, /^[0-9a-f]{64}$/);
    assert.equal(archive.url, `https://github.com/BtbN/FFmpeg-Builds/releases/download/${pin.releaseTag}/${archive.name}`);
  }
});

test("both archives are built from the same FFmpeg commit", () => {
  const { windows, linux } = readFfmpegPin();
  const commit = (name) => name.match(/-g([0-9a-f]+)-/)?.[1];
  assert.ok(commit(windows.name));
  assert.equal(commit(linux.name), commit(windows.name));
});

test("a pin without the Linux archive is reported by name", () => {
  const buildScript = `$ffmpegReleaseTag = "autobuild-1"\n$ffmpegArchiveName = "a.zip"\n$ffmpegArchiveSha256 = "${"0".repeat(64)}"\n`;
  assert.throws(() => readFfmpegPin(buildScript), /ffmpegLinuxArchiveName was not found/);
});

test("a SHA-256 that is not 64 hex characters is rejected", () => {
  const buildScript = [
    `$ffmpegReleaseTag = "autobuild-1"`,
    `$ffmpegArchiveName = "a.zip"`,
    `$ffmpegArchiveSha256 = "${"0".repeat(64)}"`,
    `$ffmpegLinuxArchiveName = "b.tar.xz"`,
    `$ffmpegLinuxArchiveSha256 = "abc"`,
  ].join("\n");
  assert.throws(() => readFfmpegPin(buildScript), /ffmpegLinuxArchiveSha256.*64-character/);
});
