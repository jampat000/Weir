import { expect, it } from "vitest";

import { asBoolean, asNumber, asString, parseActivityDetail } from "./detail";

it("parses a JSON object and nothing else", () => {
  expect(parseActivityDetail(' {"ok": true}')).toEqual({ ok: true });
  expect(parseActivityDetail("Removed 3 files")).toBeNull();
  expect(parseActivityDetail("{not json")).toBeNull();
  expect(parseActivityDetail("[1, 2]")).toBeNull();
  expect(parseActivityDetail(null)).toBeNull();
});

it("reads loose values", () => {
  expect(asString("  a.mkv ")).toBe("a.mkv");
  expect(asString("   ")).toBeNull();
  expect(asNumber("42")).toBe(42);
  expect(asNumber("")).toBeNull();
  expect(asNumber(Number.POSITIVE_INFINITY)).toBeNull();
  expect(asBoolean("false")).toBe(false);
  expect(asBoolean(1)).toBeNull();
});
