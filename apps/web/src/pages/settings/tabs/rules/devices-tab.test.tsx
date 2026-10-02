import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, expect, it, vi } from "vitest";

import * as authQueries from "../../../../lib/auth/queries";
import * as directPlayApi from "../../../../lib/processing/direct-play-api";
import { DevicesTab } from "./devices-tab";

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

afterEach(() => {
  vi.restoreAllMocks();
});

it("shows the device checklist open, with its save note", async () => {
  vi.spyOn(authQueries, "useMeQuery").mockReturnValue({
    data: { role: "operator" },
  } as ReturnType<typeof authQueries.useMeQuery>);
  vi.spyOn(directPlayApi, "fetchDirectPlayDevices").mockResolvedValue({
    customised: false,
    devices: [
      {
        id: "iphone",
        name: "iPhone",
        source: "https://support.apple.com/specs/iphone (checked 2026-08-06)",
        note: "The built-in video player.",
        selected: false,
      },
    ],
  });

  render(<DevicesTab />, { wrapper });

  expect(
    await screen.findByRole("heading", { name: "Devices you watch on" }),
  ).toBeInTheDocument();
  expect(screen.getByLabelText("iPhone")).toBeVisible();
  expect(screen.getByTestId("settings-save-model")).toHaveTextContent(
    "Nothing changes until you press Save.",
  );
});
