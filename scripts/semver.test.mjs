// node --test scripts/semver.test.mjs
import assert from "node:assert/strict";
import { test } from "node:test";

import { compareSemver, isPrerelease, parseSemver } from "./semver.mjs";

const older = (left, right) => compareSemver(parseSemver(left), parseSemver(right)) < 0;

test("a stable version has no prerelease identifiers", () => {
  const parsed = parseSemver("v1.2.3");
  assert.equal(parsed.version, "1.2.3");
  assert.equal(isPrerelease(parsed), false);
});

test("a prerelease version keeps its identifiers", () => {
  const parsed = parseSemver("1.0.0-rc.1");
  assert.deepEqual(parsed.prerelease, ["rc", "1"]);
  assert.equal(isPrerelease(parsed), true);
});

for (const bad of ["", "1.0", "1.0.0.0", "1.0.0-", "1.0.0-rc..1", "1.0.0-rc.01", "01.0.0", "1.0.0+build", "1.0.0-rc.1+build", "junk"]) {
  test(`"${bad}" does not parse`, () => {
    assert.equal(parseSemver(bad), null);
  });
}

test("release candidates order by their number", () => {
  assert.ok(older("1.0.0-rc.1", "1.0.0-rc.2"));
  assert.ok(older("1.0.0-rc.9", "1.0.0-rc.10"));
});

test("a stable release is newer than its own prereleases", () => {
  assert.ok(older("1.0.0-rc.9", "1.0.0"));
  assert.ok(older("1.0.0-beta.1", "1.0.0"));
});

test("a prerelease of a later version is newer than an earlier stable release", () => {
  assert.ok(older("1.0.0", "1.0.1-rc.1"));
});

test("prerelease names order alphabetically and numbers sort before names", () => {
  assert.ok(older("1.0.0-alpha.1", "1.0.0-beta.1"));
  assert.ok(older("1.0.0-beta.1", "1.0.0-rc.1"));
  assert.ok(older("1.0.0-1", "1.0.0-alpha"));
});

test("a prerelease with more identifiers is newer than its prefix", () => {
  assert.ok(older("1.0.0-rc", "1.0.0-rc.1"));
});

test("version numbers compare as numbers, not text", () => {
  assert.ok(older("1.9.0", "1.10.0"));
  assert.ok(older("2.0.0", "10.0.0"));
});

test("equal versions compare as equal", () => {
  assert.equal(compareSemver(parseSemver("1.0.0-rc.1"), parseSemver("v1.0.0-rc.1")), 0);
});
