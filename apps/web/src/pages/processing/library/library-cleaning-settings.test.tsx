import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, expect, it, vi } from "vitest";

import * as api from "../../../lib/processing/library-mode-api";
import { LibraryCleaningSettings } from "./library-cleaning-settings";

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

const settings: api.LibrarySettings = {
  library_folders: ["D:\\Media\\Movies"],
  library_schedule_enabled: false,
  clean_hardlinked_files: false,
  skip_if_manager_would_redownload: true,
};

afterEach(() => vi.restoreAllMocks());

it("asks before the daily clean is switched on, and sends the confirmation with it", async () => {
  vi.spyOn(api, "fetchLibrarySettings").mockResolvedValue(settings);
  const setSchedule = vi
    .spyOn(api, "setLibrarySchedule")
    .mockResolvedValue({ ...settings, library_schedule_enabled: true });

  render(<LibraryCleaningSettings libraryId={3} editable />, { wrapper });

  expect(await screen.findByText("D:\\Media\\Movies")).toBeInTheDocument();
  const daily = screen.getByRole("radiogroup", {
    name: /Check and clean once a day/,
  });
  fireEvent.click(within(daily).getByRole("radio", { name: "On" }));
  // Nothing is switched on until the person has read what it does.
  expect(setSchedule).not.toHaveBeenCalled();
  expect(screen.getByRole("alertdialog")).toHaveTextContent(
    "Removed tracks cannot be put back.",
  );

  fireEvent.click(screen.getByRole("button", { name: "Switch it on" }));
  await waitFor(() => expect(setSchedule).toHaveBeenCalledWith(3, true, true));
});

it("adds a folder to the ones already saved", async () => {
  vi.spyOn(api, "fetchLibrarySettings").mockResolvedValue(settings);
  const save = vi.spyOn(api, "saveLibraryFolders").mockResolvedValue(settings);

  render(<LibraryCleaningSettings libraryId={3} editable />, { wrapper });

  fireEvent.change(await screen.findByLabelText("Folder to add"), {
    target: { value: "E:\\More Movies" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Add folder" }));
  await waitFor(() =>
    expect(save).toHaveBeenCalledWith(3, [
      "D:\\Media\\Movies",
      "E:\\More Movies",
    ]),
  );
});
