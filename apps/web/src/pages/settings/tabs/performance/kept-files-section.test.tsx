import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, expect, it, vi } from "vitest";

import * as api from "../../../../lib/processing/kept-files-api";
import { KeptFilesSection } from "./kept-files-section";

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

afterEach(() => {
  vi.restoreAllMocks();
});

it("says nothing is kept when the list is empty", async () => {
  vi.spyOn(api, "fetchKeptFiles").mockResolvedValue({ files: [] });

  render(<KeptFilesSection />, { wrapper });

  expect(
    await screen.findByText("No files are kept right now."),
  ).toBeInTheDocument();
});

it("lists each kept file with its full name, library and size", async () => {
  vi.spyOn(api, "fetchKeptFiles").mockResolvedValue({
    files: [
      {
        id: 7,
        library_id: 1,
        library_name: "Movies",
        relative_path: "Film (2024)/Film.2024.1080p.WEB-DL.mkv",
        size_bytes: 4_000_000_000,
        kept_at: "2026-09-26T00:00:00Z",
      },
    ],
  });

  render(<KeptFilesSection />, { wrapper });

  expect(await screen.findByTestId("kept-file-row-7")).toBeInTheDocument();
  expect(screen.getByText("Movies")).toBeInTheDocument();
  expect(screen.getByText("3.73 GB")).toBeInTheDocument();
  expect(
    screen.getByRole("button", { name: "Process again" }),
  ).toBeInTheDocument();
});

it("shows a load error instead of a blank panel when kept files fail to load", async () => {
  vi.spyOn(api, "fetchKeptFiles").mockRejectedValue(new Error("network down"));

  render(<KeptFilesSection />, { wrapper });

  expect(await screen.findByTestId("settings-load-error")).toBeInTheDocument();
});

it("processing a kept file again shows the server's own message and removes the row", async () => {
  vi.spyOn(api, "fetchKeptFiles").mockResolvedValue({
    files: [
      {
        id: 7,
        library_id: 1,
        library_name: "Movies",
        relative_path: "Film/film.mkv",
        size_bytes: 100,
        kept_at: "2026-09-26T00:00:00Z",
      },
    ],
  });
  const processAgain = vi.spyOn(api, "processKeptFileAgain").mockResolvedValue({
    detail:
      "Weir is checking this file's library now and will queue it once it is ready.",
  });

  render(<KeptFilesSection />, { wrapper });
  fireEvent.click(await screen.findByRole("button", { name: "Process again" }));

  expect(
    await screen.findByText(
      "Weir is checking this file's library now and will queue it once it is ready.",
    ),
  ).toBeInTheDocument();
  expect(processAgain).toHaveBeenCalledWith(7);
});

it("shows the server's refusal when processing a kept file again fails", async () => {
  vi.spyOn(api, "fetchKeptFiles").mockResolvedValue({
    files: [
      {
        id: 7,
        library_id: 1,
        library_name: "Movies",
        relative_path: "Film/film.mkv",
        size_bytes: 100,
        kept_at: "2026-09-26T00:00:00Z",
      },
    ],
  });
  vi.spyOn(api, "processKeptFileAgain").mockRejectedValue(
    new Error("Weir has no kept file with that id."),
  );

  render(<KeptFilesSection />, { wrapper });
  fireEvent.click(await screen.findByRole("button", { name: "Process again" }));

  await waitFor(() =>
    expect(
      screen.getByText("Weir has no kept file with that id."),
    ).toBeInTheDocument(),
  );
});
