import { expect, it } from "vitest";

import {
  formatDuration,
  formatProcessingSpeed,
  formatSavings,
} from "./pass-detail";

it("keeps the speed's full precision and names the unit", () => {
  expect(formatProcessingSpeed("123.456789x")).toBe("123.456789x realtime");
  expect(formatProcessingSpeed(" 0.987654x ")).toBe("0.987654x realtime");
  expect(formatProcessingSpeed("N/A")).toBe("N/A");
  expect(formatProcessingSpeed("")).toBeNull();
});

it("reads durations the way a person says them", () => {
  expect(formatDuration(45)).toBe("45s");
  expect(formatDuration(187)).toBe("3m 07s");
  expect(formatDuration(3900)).toBe("1h 05m");
  expect(formatDuration(-1)).toBeNull();
});

it("says what a pass did to the size", () => {
  const gb = 1024 ** 3;
  expect(formatSavings(5 * gb, 4 * gb)).toBe("Saved 1.00 GB (20.0%)");
  expect(formatSavings(4 * gb, 4 * gb)).toBe("No size change");
  expect(formatSavings(4 * gb, 4 * gb + 1024)).toBe(
    "Size basically unchanged (1.0 KB container overhead)",
  );
  expect(formatSavings(null, 1)).toBeNull();
});
