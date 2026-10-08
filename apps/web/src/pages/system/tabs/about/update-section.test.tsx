import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { UpdateStatus } from "../../../../lib/settings/types";
import { UpdateSection } from "./update-section";

const mocks = vi.hoisted(() => ({
  useUpdateStatusQuery: vi.fn(),
}));

vi.mock("../../../../lib/settings/queries", () => ({
  useUpdateStatusQuery: () => mocks.useUpdateStatusQuery(),
  useUpdateSettingsQuery: () => ({ data: undefined }),
  useAppSettingsQuery: () => ({ data: { app_timezone: "UTC" } }),
}));

const LIMITED =
  "GitHub is limiting update checks from your network right now. Weir will check again at 11:35 pm.";

function status(
  limited: Pick<UpdateStatus, "summary"> &
    Pick<UpdateStatus, "latest_version" | "published_at" | "retry_at">,
): UpdateStatus {
  return {
    current_version: "3.2.16",
    install_type: "source",
    in_app_upgrade_supported: false,
    status: "rate_limited",
    ...limited,
  };
}

function showStatus(update: UpdateStatus) {
  mocks.useUpdateStatusQuery.mockReturnValue({
    data: update,
    isPending: false,
    isFetching: false,
    refetch: vi.fn(),
  });
  render(<UpdateSection />);
}

describe("UpdateSection while GitHub limits update checks", () => {
  beforeEach(() => {
    mocks.useUpdateStatusQuery.mockReset();
  });

  it("says plainly when Weir will check again", () => {
    showStatus(
      status({
        summary: LIMITED,
        retry_at: "2026-01-15T23:35:00Z",
      }),
    );

    const line = screen.getByTestId("suite-settings-release-status");
    expect(line).toHaveTextContent("Limited by GitHub");
    expect(line).toHaveTextContent(LIMITED);
    expect(
      screen.queryByTestId("suite-settings-last-known-release"),
    ).toBeNull();
  });

  it("keeps the last release it knew of in view", () => {
    showStatus(
      status({
        summary: LIMITED,
        latest_version: "3.3.0",
        published_at: "2026-10-01T10:00:00Z",
      }),
    );

    expect(
      screen.getByTestId("suite-settings-last-known-release"),
    ).toHaveTextContent(/Latest known: 3\.3\.0, published .*2026/);
  });

  it("still offers Check again, which the server answers without asking GitHub", () => {
    showStatus(status({ summary: LIMITED }));

    expect(screen.getByRole("button", { name: "Check again →" })).toBeEnabled();
  });
});
