import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type {
  FetcherArrOperatorSettingsOut,
  FetcherArrSearchLane,
} from "../../lib/fetcher/arr-operator-settings/types";
import { FETCHER_TAB_SONARR_LABEL } from "./fetcher-display-names";
import { FetcherCurrentSearchSetupSection } from "./fetcher-automation-summary";

const lane = (overrides: Partial<FetcherArrSearchLane> = {}): FetcherArrSearchLane => ({
  enabled: true,
  max_items_per_run: 50,
  retry_delay_minutes: 1440,
  schedule_enabled: false,
  schedule_days: "",
  schedule_start: "00:00",
  schedule_end: "23:59",
  schedule_interval_seconds: 3600,
  ...overrides,
});

const baseArr = (): FetcherArrOperatorSettingsOut => ({
  sonarr_missing: lane(),
  sonarr_upgrade: lane(),
  radarr_missing: lane(),
  radarr_upgrade: lane(),
  sonarr_connection: {
    enabled: false,
    base_url: "",
    api_key_is_saved: false,
    last_test_ok: null,
    last_test_at: null,
    last_test_detail: null,
    status_headline: "Connection status: Not checked yet",
    effective_base_url: null,
  },
  radarr_connection: {
    enabled: false,
    base_url: "",
    api_key_is_saved: false,
    last_test_ok: null,
    last_test_at: null,
    last_test_detail: null,
    status_headline: "Connection status: Not checked yet",
    effective_base_url: null,
  },
  schedule_timezone: "UTC",
  connection_note: "",
  interval_restart_note: "",
  sonarr_server_configured: true,
  radarr_server_configured: true,
  sonarr_server_url: null,
  radarr_server_url: null,
  updated_at: "2026-04-11T12:00:00Z",
});

function sonarrCardRoot(): HTMLElement {
  const summary = screen.getByTestId("fetcher-automation-summary");
  const h = within(summary).getByRole("heading", { name: FETCHER_TAB_SONARR_LABEL });
  const root = h.parentElement;
  if (!root) {
    throw new Error("expected Sonarr card root");
  }
  return root;
}

describe("FetcherCurrentSearchSetupSection", () => {
  it("shows one run interval when missing and upgrade lanes match", () => {
    const arr = baseArr();
    render(<FetcherCurrentSearchSetupSection arr={arr} />);
    const card = sonarrCardRoot();
    expect(within(card).getAllByText("Every hour")).toHaveLength(1);
  });

  it("shows missing vs upgrade run intervals when lanes differ", () => {
    const arr = baseArr();
    arr.sonarr_missing = lane({ schedule_interval_seconds: 3600 });
    arr.sonarr_upgrade = lane({ schedule_interval_seconds: 7200 });
    render(<FetcherCurrentSearchSetupSection arr={arr} />);
    const card = sonarrCardRoot();
    expect(card.textContent).toContain("Every hour");
    expect(card.textContent).toContain("Every 2 hours");
  });
});
