import { describe, expect, it } from "vitest";
import { fetcherArrOperatorSettingsPath } from "./arr-operator-settings-api";

describe("fetcherArrOperatorSettingsPath", () => {
  it("points at the Fetcher module operator settings route", () => {
    expect(fetcherArrOperatorSettingsPath()).toBe("/api/v1/fetcher/arr-operator-settings");
  });
});
