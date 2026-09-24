import { expect, it } from "vitest";

import { formatBytes } from "./bytes";

it("uses binary units with just enough decimals", () => {
  expect(formatBytes(0)).toBe("0 B");
  expect(formatBytes(512)).toBe("512 B");
  expect(formatBytes(1536)).toBe("1.5 KB");
  expect(formatBytes(318 * 1024 * 1024)).toBe("318 MB");
  expect(formatBytes(2.5 * 1024 ** 3)).toBe("2.50 GB");
});

it("keeps the sign and says nothing for a missing size", () => {
  expect(formatBytes(-2048)).toBe("-2.0 KB");
  expect(formatBytes(null)).toBe("");
  expect(formatBytes(undefined)).toBe("");
  expect(formatBytes(Number.NaN)).toBe("");
});
