// node --test scripts/check-release-assets-single-version.test.mjs
import assert from "node:assert/strict";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { test } from "node:test";

import { findOtherVersionAssets } from "./check-release-assets-single-version.mjs";

function makeDir() {
  return mkdtempSync(path.join(tmpdir(), "weir-asset-check-"));
}

test("a directory holding only the released version passes", () => {
  const dir = makeDir();
  try {
    writeFileSync(path.join(dir, "Weir-1.0.1-full.nupkg"), "bytes");
    writeFileSync(path.join(dir, "Weir-1.0.1-delta.nupkg"), "bytes");
    writeFileSync(path.join(dir, "releases.win.json"), '{"Assets":[{"Version":"1.0.1","FileName":"Weir-1.0.1-full.nupkg"}]}');
    writeFileSync(path.join(dir, "RELEASES"), "﻿SHA1 Weir-1.0.1-full.nupkg 123");
    assert.deepEqual(findOtherVersionAssets(dir, "1.0.1"), []);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test("a leftover package for another version is reported", () => {
  const dir = makeDir();
  try {
    writeFileSync(path.join(dir, "Weir-1.0.0-full.nupkg"), "bytes");
    writeFileSync(path.join(dir, "Weir-1.0.1-full.nupkg"), "bytes");
    const problems = findOtherVersionAssets(dir, "1.0.1");
    assert.equal(problems.length, 1);
    assert.match(problems[0], /Weir-1\.0\.0-full\.nupkg/);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test("a stale feed entry is reported even when the file itself was removed", () => {
  const dir = makeDir();
  try {
    writeFileSync(path.join(dir, "Weir-1.0.1-full.nupkg"), "bytes");
    writeFileSync(
      path.join(dir, "releases.win.json"),
      '{"Assets":[{"Version":"1.0.1","FileName":"Weir-1.0.1-full.nupkg"},{"Version":"1.0.0","FileName":"Weir-1.0.0-full.nupkg"}]}',
    );
    const problems = findOtherVersionAssets(dir, "1.0.1");
    assert.equal(problems.length, 1);
    assert.match(problems[0], /releases\.win\.json lists Weir-1\.0\.0-full\.nupkg for version 1\.0\.0/);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test("a stale legacy RELEASES line is reported", () => {
  const dir = makeDir();
  try {
    writeFileSync(path.join(dir, "Weir-1.0.1-full.nupkg"), "bytes");
    writeFileSync(path.join(dir, "RELEASES"), "﻿SHA1 Weir-1.0.0-full.nupkg 123\nSHA2 Weir-1.0.1-full.nupkg 456");
    const problems = findOtherVersionAssets(dir, "1.0.1");
    assert.equal(problems.length, 1);
    assert.match(problems[0], /RELEASES has a line for another version/);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test("missing feed files are tolerated when no packages disagree", () => {
  const dir = makeDir();
  try {
    writeFileSync(path.join(dir, "Weir-1.0.1-full.nupkg"), "bytes");
    assert.deepEqual(findOtherVersionAssets(dir, "1.0.1"), []);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
