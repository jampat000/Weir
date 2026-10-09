import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { AppSettings } from "../../../../lib/settings/types";
import type { SystemOverview } from "../../../../lib/system/system-stats-types";
import { testOverview } from "../../../processing/dashboard/system/test-stats";
import { ThisWeirSection } from "./this-weir-section";

const mocks = vi.hoisted(() => ({ overview: vi.fn() }));

vi.mock("../../../../lib/system/use-system-stats", () => ({
  useSystemOverviewQuery: () => mocks.overview(),
}));
vi.mock("../../../../lib/system/readiness-queries", () => ({
  useSystemReadinessQuery: () => ({ data: undefined }),
}));
vi.mock("../../../../lib/settings/queries", () => ({
  useUpdateStatusQuery: () => ({ data: undefined }),
}));
vi.mock("../../../../lib/ui/mm-format-date", () => ({
  useAppDateFormatter: () => (iso: string | null | undefined) =>
    `on ${iso ?? ""}`,
}));

const settings = { setup_wizard_state: "completed" } as AppSettings;

function showOverview(overview: Partial<SystemOverview>) {
  mocks.overview.mockReturnValue({ data: { ...testOverview, ...overview } });
  render(
    <MemoryRouter>
      <ThisWeirSection settings={settings} />
    </MemoryRouter>,
  );
}

describe("System › About: the copy taken before an update", () => {
  beforeEach(() => {
    mocks.overview.mockReset();
  });

  it("says where Weir saved a copy of its data before it last updated, and when", () => {
    showOverview({
      last_update_backup: {
        path: "C:\\ProgramData\\Weir\\backups\\pre-update\\weir-0076-to-1.0.0-rc.13-20261010T090000Z.db",
        taken_at: "2026-10-10T09:00:00Z",
      },
    });

    const note = screen.getByTestId("about-update-backup");
    expect(note).toHaveTextContent(
      "Before updating, Weir saved a copy of its data at C:\\ProgramData\\Weir\\backups\\pre-update\\weir-0076-to-1.0.0-rc.13-20261010T090000Z.db.",
    );
    expect(note).toHaveAttribute("title", "Saved on 2026-10-10T09:00:00Z");
  });

  it("says nothing about a copy when no update has made one", () => {
    showOverview({ last_update_backup: null });

    expect(screen.queryByTestId("about-update-backup")).not.toBeInTheDocument();
  });
});
