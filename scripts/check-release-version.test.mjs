// node --test scripts/check-release-version.test.mjs
import assert from "node:assert/strict";
import { test } from "node:test";

import { parseReleaseVersion } from "./check-release-version.mjs";

test("a well-formed vX.Y.Z tag parses to its bare version", () => {
  assert.deepEqual(parseReleaseVersion("v3.2.10"), { ok: true, version: "3.2.10" });
});

test("a tag with no leading v also parses", () => {
  assert.deepEqual(parseReleaseVersion("3.2.10"), { ok: true, version: "3.2.10" });
});

test("a zero version component is accepted", () => {
  assert.deepEqual(parseReleaseVersion("v0.0.1"), { ok: true, version: "0.0.1" });
});

for (const bad of ["v3.2", "v3.2.10.1", "v3.2.10-beta", "v3.2.10+build", "vX.Y.Z", "", "v", "v01.2.3"]) {
  test(`"${bad}" is refused`, () => {
    const result = parseReleaseVersion(bad);
    assert.equal(result.ok, false);
    assert.match(result.reason, /not a release version/);
  });
}
