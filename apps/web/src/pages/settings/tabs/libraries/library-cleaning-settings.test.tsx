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

import * as api from "../../../../lib/processing/library-mode-api";
import { useLibraryCleaningDraft } from "./library-cleaning-draft";
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
  keep_original_after_clean: false,
  originals_folder: "",
};

/** The settings as the library editor holds them, with the editor's Save standing in as a button. */
function Editor() {
  const cleaning = useLibraryCleaningDraft(3);
  return (
    <>
      <LibraryCleaningSettings libraryId={3} cleaning={cleaning} editable />
      <button type="button" onClick={() => void cleaning.save()}>
        Editor save
      </button>
    </>
  );
}

afterEach(() => vi.restoreAllMocks());

it("asks before the daily clean is switched on, and sends the confirmation with it on Save", async () => {
  vi.spyOn(api, "fetchLibrarySettings").mockResolvedValue(settings);
  const setSchedule = vi
    .spyOn(api, "setLibrarySchedule")
    .mockResolvedValue({ ...settings, library_schedule_enabled: true });

  render(<Editor />, { wrapper });

  expect(await screen.findByText("D:\\Media\\Movies")).toBeInTheDocument();
  const daily = screen.getByRole("radiogroup", {
    name: /Check and clean once a day/,
  });
  fireEvent.click(within(daily).getByRole("radio", { name: "On" }));
  // Nothing is switched on until the person has read what it does.
  expect(screen.getByRole("alertdialog")).toHaveTextContent(
    "Removed tracks cannot be put back.",
  );
  fireEvent.click(screen.getByRole("button", { name: "Switch it on" }));
  expect(setSchedule).not.toHaveBeenCalled();

  fireEvent.click(screen.getByRole("button", { name: "Editor save" }));
  await waitFor(() => expect(setSchedule).toHaveBeenCalledWith(3, true, true));
});

it("adds a folder to the ones already saved, and saves it only with the library", async () => {
  vi.spyOn(api, "fetchLibrarySettings").mockResolvedValue(settings);
  const save = vi.spyOn(api, "saveLibrarySettings").mockResolvedValue(settings);

  render(<Editor />, { wrapper });

  fireEvent.change(await screen.findByLabelText("Folder to add"), {
    target: { value: "E:\\More Movies" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Add folder" }));
  expect(screen.getByText("E:\\More Movies")).toBeInTheDocument();
  expect(save).not.toHaveBeenCalled();

  fireEvent.click(screen.getByRole("button", { name: "Editor save" }));
  await waitFor(() =>
    expect(save).toHaveBeenCalledWith(3, {
      library_folders: ["D:\\Media\\Movies", "E:\\More Movies"],
      clean_hardlinked_files: false,
      skip_if_manager_would_redownload: true,
      keep_original_after_clean: false,
      originals_folder: "",
    }),
  );
});

it("shows the originals folder field only once keep-the-original is switched on, and saves it", async () => {
  vi.spyOn(api, "fetchLibrarySettings").mockResolvedValue(settings);
  const save = vi.spyOn(api, "saveLibrarySettings").mockResolvedValue(settings);

  render(<Editor />, { wrapper });
  await screen.findByText("D:\\Media\\Movies");
  expect(screen.queryByLabelText("Originals folder")).not.toBeInTheDocument();

  const keepOriginal = screen.getByRole("radiogroup", {
    name: "Keep the original after cleaning",
  });
  fireEvent.click(within(keepOriginal).getByRole("radio", { name: "On" }));

  expect(screen.getByText(/Weir moves the original into/)).toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("Originals folder"), {
    target: { value: "D:\\Media\\Movies\\.weir-originals" },
  });

  fireEvent.click(screen.getByRole("button", { name: "Editor save" }));
  await waitFor(() =>
    expect(save).toHaveBeenCalledWith(3, {
      library_folders: ["D:\\Media\\Movies"],
      clean_hardlinked_files: false,
      skip_if_manager_would_redownload: true,
      keep_original_after_clean: true,
      originals_folder: "D:\\Media\\Movies\\.weir-originals",
    }),
  );
});

it("says so when these settings could not be loaded", async () => {
  vi.spyOn(api, "fetchLibrarySettings").mockRejectedValue(new Error("boom"));

  render(<Editor />, { wrapper });

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Weir couldn’t load these settings. Reload the page to try again.",
  );
});
