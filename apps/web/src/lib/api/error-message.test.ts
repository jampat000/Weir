import { expect, it } from "vitest";

import { errorMessage } from "./error-message";

it("shows the error's own message", () => {
  expect(errorMessage(new Error("Disk full"), "Could not save.")).toBe(
    "Disk full",
  );
  expect(errorMessage({ message: "Refused" }, "Could not save.")).toBe(
    "Refused",
  );
  expect(errorMessage("Timed out", "Could not save.")).toBe("Timed out");
});

it("falls back when there is nothing to show", () => {
  expect(errorMessage(new Error("   "), "Could not save.")).toBe(
    "Could not save.",
  );
  expect(errorMessage(null, "Could not save.")).toBe("Could not save.");
  expect(errorMessage(42, "Could not save.")).toBe("Could not save.");
});
