// node --test scripts/check-event-titles.test.mjs
import assert from "node:assert/strict";
import { test } from "node:test";

import { eventTypesIn, titledTypesIn, untitled } from "./check-event-titles.mjs";

test("event types are read from the server's string constants", () => {
  const source = `
    public const string AuthLogout = "auth.logout";
    public const string SystemReconciliationRepair = "system.reconciliation.repair";
    public const string JobKindPrefix = "processing.";
  `;
  assert.deepEqual(eventTypesIn(source), ["auth.logout", "system.reconciliation.repair"]);
});

test("titles are read from the web table's keys, not its values", () => {
  const source = `export const EVENT_LABELS = {
  "auth.logout": "Sign-out finished",
  "processing.worker_failure":
    "A processing job failed",
};`;
  assert.deepEqual([...titledTypesIn(source)].sort(), ["auth.logout", "processing.worker_failure"]);
});

test("an event type with no title is named", () => {
  assert.deepEqual(untitled(["a.b", "c.d"], new Set(["a.b"])), ["c.d"]);
  assert.deepEqual(untitled(["a.b"], new Set(["a.b"])), []);
});
