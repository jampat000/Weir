// node --test scripts/find-previous-release.test.mjs
import assert from "node:assert/strict";
import { test } from "node:test";

import { findPreviousVersion } from "./find-previous-release.mjs";

test("the first release has no previous release", () => {
  assert.equal(findPreviousVersion([], "1.0.0-rc.1"), null);
});

test("the previous release is the newest older one, by version and not by list order", () => {
  assert.equal(findPreviousVersion(["v1.0.0-rc.1", "v1.0.0-rc.10", "v1.0.0-rc.9"], "1.0.0-rc.11"), "1.0.0-rc.10");
});

test("a stable release is built against the newest release candidate before it", () => {
  assert.equal(findPreviousVersion(["v1.0.0-rc.1", "v1.0.0-rc.2"], "1.0.0"), "1.0.0-rc.2");
});

test("a newer published version is never the previous release", () => {
  assert.equal(findPreviousVersion(["v4.0.0", "v1.0.0-rc.1"], "1.0.0-rc.2"), "1.0.0-rc.1");
});

test("when only newer versions are published there is no previous release", () => {
  assert.equal(findPreviousVersion(["v4.0.0", "v1.0.0"], "1.0.0-rc.1"), null);
});

test("the version being released is not its own previous release", () => {
  assert.equal(findPreviousVersion(["v1.0.0-rc.1"], "1.0.0-rc.1"), null);
});

test("tags that are not versions are ignored", () => {
  assert.equal(findPreviousVersion(["nightly", "v1.0", "v1.0.0-rc.1"], "1.0.0-rc.2"), "1.0.0-rc.1");
});

test("a release version that is not a version is refused", () => {
  assert.throws(() => findPreviousVersion([], "junk"), /not a release version/);
});
