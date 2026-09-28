// node --test scripts/prune-release-feed.test.mjs
import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { test } from "node:test";

import { filterAssetFeed, filterLegacyReleases, nupkgsToRemove, prune } from "./prune-release-feed.mjs";

// A real `vpk pack` output directory, captured once from a throwaway local pack: v1.0.0 packed alone,
// then v1.0.1 packed against it (a real delta), before any pruning.
const FEED_JSON =
  '{"Assets":[{"PackageId":"TestApp","Version":"1.0.1","Type":"Full","FileName":"TestApp-1.0.1-full.nupkg","SHA1":"F0C","SHA256":"6D2","Size":2143523},' +
  '{"PackageId":"TestApp","Version":"1.0.1","Type":"Delta","FileName":"TestApp-1.0.1-delta.nupkg","SHA1":"9C0","SHA256":"381","Size":3236},' +
  '{"PackageId":"TestApp","Version":"1.0.0","Type":"Full","FileName":"TestApp-1.0.0-full.nupkg","SHA1":"A7A","SHA256":"20D","Size":2143491}]}';
const LEGACY_RELEASES =
  "﻿A7A19A432D4E218DDFACA9D500B8A92C6DA0958F TestApp-1.0.0-full.nupkg 2143491\n" +
  "F0CCB5C5D964ABD84A7DCA0BBDC75F5EA823CF2D TestApp-1.0.1-full.nupkg 2143523";

test("nupkgsToRemove keeps the released version's packages and the portable zip", () => {
  const entries = ["TestApp-1.0.0-full.nupkg", "TestApp-1.0.1-full.nupkg", "TestApp-1.0.1-delta.nupkg", "TestApp-win-Portable.zip"];
  assert.deepEqual(nupkgsToRemove(entries, "1.0.1"), ["TestApp-1.0.0-full.nupkg"]);
});

test("nupkgsToRemove is a no-op when only the released version is present", () => {
  const entries = ["TestApp-1.0.0-full.nupkg", "TestApp-win-Portable.zip"];
  assert.deepEqual(nupkgsToRemove(entries, "1.0.0"), []);
});

test("filterAssetFeed drops every asset for another version and keeps the feed shape", () => {
  const { json, kept, dropped } = filterAssetFeed(FEED_JSON, "1.0.1");
  assert.equal(kept.length, 2);
  assert.equal(dropped.length, 1);
  assert.equal(dropped[0].FileName, "TestApp-1.0.0-full.nupkg");
  const feed = JSON.parse(json);
  assert.deepEqual(
    feed.Assets.map((asset) => asset.FileName),
    ["TestApp-1.0.1-full.nupkg", "TestApp-1.0.1-delta.nupkg"],
  );
});

test("filterLegacyReleases keeps the BOM and drops the other version's line", () => {
  const { text, kept, dropped } = filterLegacyReleases(LEGACY_RELEASES, "1.0.1");
  assert.equal(kept.length, 1);
  assert.equal(dropped.length, 1);
  assert.match(dropped[0], /TestApp-1\.0\.0-full\.nupkg/);
  assert.equal(text, "﻿F0CCB5C5D964ABD84A7DCA0BBDC75F5EA823CF2D TestApp-1.0.1-full.nupkg 2143523");
});

test("prune removes the other version's package and rewrites both feeds on disk", () => {
  const dir = mkdtempSync(path.join(tmpdir(), "weir-prune-"));
  try {
    writeFileSync(path.join(dir, "TestApp-1.0.0-full.nupkg"), "old full package bytes");
    writeFileSync(path.join(dir, "TestApp-1.0.1-full.nupkg"), "new full package bytes");
    writeFileSync(path.join(dir, "TestApp-1.0.1-delta.nupkg"), "delta package bytes");
    writeFileSync(path.join(dir, "TestApp-win-Portable.zip"), "portable bytes");
    writeFileSync(path.join(dir, "releases.win.json"), FEED_JSON);
    writeFileSync(path.join(dir, "RELEASES"), LEGACY_RELEASES);

    const result = prune(dir, "1.0.1");

    assert.deepEqual(result.removedFiles, ["TestApp-1.0.0-full.nupkg"]);
    assert.equal(result.droppedAssets.length, 1);
    assert.equal(result.droppedLines.length, 1);
    assert.throws(() => readFileSync(path.join(dir, "TestApp-1.0.0-full.nupkg")));
    assert.equal(readFileSync(path.join(dir, "TestApp-win-Portable.zip"), "utf8"), "portable bytes");

    const feed = JSON.parse(readFileSync(path.join(dir, "releases.win.json"), "utf8"));
    assert.deepEqual(
      feed.Assets.map((asset) => asset.Version),
      ["1.0.1", "1.0.1"],
    );

    const legacy = readFileSync(path.join(dir, "RELEASES"), "utf8");
    assert.ok(legacy.startsWith("﻿"));
    assert.doesNotMatch(legacy, /1\.0\.0/);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test("prune is a no-op when no other-version assets are present", () => {
  const dir = mkdtempSync(path.join(tmpdir(), "weir-prune-"));
  try {
    writeFileSync(path.join(dir, "TestApp-1.0.0-full.nupkg"), "full package bytes");
    const result = prune(dir, "1.0.0");
    assert.deepEqual(result, { removedFiles: [], droppedAssets: [], droppedLines: [] });
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test("prune tolerates missing feed files (a pack with no delta base yet)", () => {
  const dir = mkdtempSync(path.join(tmpdir(), "weir-prune-"));
  try {
    writeFileSync(path.join(dir, "TestApp-1.0.0-full.nupkg"), "full package bytes");
    assert.doesNotThrow(() => prune(dir, "1.0.0"));
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
