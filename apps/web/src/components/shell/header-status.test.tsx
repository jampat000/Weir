import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { HeaderStatus } from "./header-status";

const pause = {
  paused: false,
  paused_until: null as string | null,
  reason: "",
};
const readiness = { isError: false };

vi.mock("../../lib/pause/pause-queries", () => ({
  usePauseQuery: () => ({ data: pause }),
}));
vi.mock("../../lib/system/readiness-queries", () => ({
  useSystemReadinessQuery: () => ({ isError: readiness.isError }),
}));
vi.mock("../../lib/ui/mm-format-date", async (importActual) => ({
  ...(await importActual<typeof import("../../lib/ui/mm-format-date")>()),
  useAppDateFormatter: () => (iso: string | null | undefined) =>
    `4:20 pm (${iso})`,
  useAppClockFormatter: () => () => "4:20 pm",
}));

describe("HeaderStatus", () => {
  beforeEach(() => {
    pause.paused = false;
    pause.paused_until = null;
    pause.reason = "";
    readiness.isError = false;
  });

  it("shows nothing while all is well, so a pill is always news", () => {
    render(<HeaderStatus />);

    expect(screen.getByRole("status")).toBeEmptyDOMElement();
  });

  it("says Paused and when it lifts, keeping the server's own sentence for hovering", () => {
    pause.paused = true;
    pause.paused_until = "2026-08-26T16:20:00Z";
    pause.reason =
      "Processing is paused. Weir will start work again at 4:20 pm.";

    render(<HeaderStatus />);

    const pill = screen.getByTestId("pause-badge");
    expect(pill).toHaveTextContent(
      "Paused · until 4:20 pm (2026-08-26T16:20:00Z)",
    );
    expect(pill).toHaveAttribute("title", pause.reason);
  });

  it("says a pause with no end lasts until it is resumed", () => {
    pause.paused = true;

    render(<HeaderStatus />);

    expect(screen.getByTestId("pause-badge")).toHaveTextContent(
      "Paused · until you resume",
    );
  });

  it("says Weir cannot be reached, and nothing else, when it does not answer", () => {
    pause.paused = true;
    readiness.isError = true;

    render(<HeaderStatus />);

    expect(screen.getByTestId("status-offline")).toHaveTextContent(
      "Can't reach Weir",
    );
    expect(screen.queryByTestId("pause-badge")).not.toBeInTheDocument();
  });
});
