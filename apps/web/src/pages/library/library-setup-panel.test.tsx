import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, expect, it, vi } from "vitest";

import * as authQueries from "../../lib/auth/queries";
import type { ProcessingLibrary } from "../../lib/processing/libraries-api";
import * as api from "../../lib/processing/library-mode-api";
import * as ruleSetsApi from "../../lib/processing/rule-sets-api";
import { LibrarySetupPanel } from "./library-setup-panel";

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return (
    <MemoryRouter>
      <QueryClientProvider client={qc}>{children}</QueryClientProvider>
    </MemoryRouter>
  );
}

const settings: api.LibrarySettings = {
  library_folders: ["D:\\Media\\Movies"],
  library_schedule_enabled: false,
  clean_hardlinked_files: false,
  skip_if_manager_would_redownload: true,
  keep_original_after_clean: false,
  originals_folder: "",
  library_rule_set_id: null,
};

const workflow = { id: 3, name: "Movies", rule_set_id: 1 } as ProcessingLibrary;

const profiles = [
  { id: 1, name: "Movies default" },
  { id: 2, name: "Strict English" },
] as ruleSetsApi.ProcessingRuleSet[];

const onClose = vi.fn();

function open(role: "operator" | "viewer" = "operator") {
  vi.spyOn(authQueries, "useMeQuery").mockReturnValue({
    data: { role },
  } as ReturnType<typeof authQueries.useMeQuery>);
  vi.spyOn(ruleSetsApi, "fetchProcessingRuleSets").mockResolvedValue(profiles);
  return render(
    <LibrarySetupPanel
      workflow={workflow}
      settings={settings}
      onClose={onClose}
    />,
    { wrapper },
  );
}

afterEach(() => {
  vi.restoreAllMocks();
  onClose.mockReset();
});

it("asks before the daily clean is switched on, and sends the confirmation with it on Save", async () => {
  const setSchedule = vi
    .spyOn(api, "setLibrarySchedule")
    .mockResolvedValue({ ...settings, library_schedule_enabled: true });
  open();

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

  fireEvent.click(screen.getByRole("button", { name: "Save" }));
  await waitFor(() => expect(setSchedule).toHaveBeenCalledWith(3, true, true));
  await waitFor(() => expect(onClose).toHaveBeenCalled());
});

it("adds a folder to the ones already saved, and saves it only on Save", async () => {
  const save = vi.spyOn(api, "saveLibrarySettings").mockResolvedValue(settings);
  open();

  fireEvent.change(screen.getByLabelText("Folder to add"), {
    target: { value: "E:\\More Movies" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Add folder" }));
  expect(screen.getByText("E:\\More Movies")).toBeInTheDocument();
  expect(save).not.toHaveBeenCalled();

  fireEvent.click(screen.getByRole("button", { name: "Save" }));
  await waitFor(() =>
    expect(save).toHaveBeenCalledWith(3, {
      library_folders: ["D:\\Media\\Movies", "E:\\More Movies"],
      clean_hardlinked_files: false,
      skip_if_manager_would_redownload: true,
      keep_original_after_clean: false,
      originals_folder: "",
      library_rule_set_id: null,
    }),
  );
});

it("follows the workflow's rules profile until another is chosen, then saves the choice", async () => {
  const save = vi.spyOn(api, "saveLibrarySettings").mockResolvedValue(settings);
  open();

  const profile = await screen.findByTestId("library-rules-profile");
  expect(
    await within(profile).findByRole("option", {
      name: "Same as the workflow (Movies default)",
    }),
  ).toBeInTheDocument();
  expect(profile).toHaveValue("");

  fireEvent.change(profile, { target: { value: "2" } });
  fireEvent.click(screen.getByRole("button", { name: "Save" }));

  await waitFor(() =>
    expect(save).toHaveBeenCalledWith(
      3,
      expect.objectContaining({ library_rule_set_id: 2 }),
    ),
  );
});

it("goes back to the workflow's profile by sending none", async () => {
  vi.spyOn(ruleSetsApi, "fetchProcessingRuleSets").mockResolvedValue(profiles);
  vi.spyOn(authQueries, "useMeQuery").mockReturnValue({
    data: { role: "operator" },
  } as ReturnType<typeof authQueries.useMeQuery>);
  const save = vi.spyOn(api, "saveLibrarySettings").mockResolvedValue(settings);
  render(
    <LibrarySetupPanel
      workflow={workflow}
      settings={{ ...settings, library_rule_set_id: 2 }}
      onClose={onClose}
    />,
    { wrapper },
  );

  const profile = await screen.findByTestId("library-rules-profile");
  await waitFor(() => expect(profile).toHaveValue("2"));
  fireEvent.change(profile, { target: { value: "" } });
  fireEvent.click(screen.getByRole("button", { name: "Save" }));

  await waitFor(() =>
    expect(save).toHaveBeenCalledWith(
      3,
      expect.objectContaining({ library_rule_set_id: null }),
    ),
  );
});

it("shows the originals folder only once keeping the original is switched on, and saves it", async () => {
  const save = vi.spyOn(api, "saveLibrarySettings").mockResolvedValue(settings);
  open();
  expect(screen.queryByLabelText("Originals folder")).not.toBeInTheDocument();

  const keepOriginal = screen.getByRole("radiogroup", {
    name: /^Files already in your library: keep the original after cleaning/,
  });
  expect(keepOriginal).toHaveAccessibleName(
    "Files already in your library: keep the original after cleaning (move it to a .weir-originals folder inside each library folder)",
  );
  fireEvent.click(within(keepOriginal).getByRole("radio", { name: "On" }));

  expect(screen.getByText(/Weir moves the original into/)).toBeInTheDocument();
  expect(
    screen.getByText(/must start with a dot \(like \.weir-originals\)/),
  ).toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("Originals folder"), {
    target: { value: "D:\\Media\\Movies\\.weir-originals" },
  });

  fireEvent.click(screen.getByRole("button", { name: "Save" }));
  await waitFor(() =>
    expect(save).toHaveBeenCalledWith(3, {
      library_folders: ["D:\\Media\\Movies"],
      clean_hardlinked_files: false,
      skip_if_manager_would_redownload: true,
      keep_original_after_clean: true,
      originals_folder: "D:\\Media\\Movies\\.weir-originals",
      library_rule_set_id: null,
    }),
  );
});

it("says why a save failed and stays open", async () => {
  vi.spyOn(api, "saveLibrarySettings").mockRejectedValue(
    new Error("Library folder '/x' overlaps this workflow's watched folder."),
  );
  open();

  fireEvent.change(screen.getByLabelText("Folder to add"), {
    target: { value: "/x" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Add folder" }));
  fireEvent.click(screen.getByRole("button", { name: "Save" }));

  expect(
    await screen.findByTestId("library-setup-save-error"),
  ).toHaveTextContent("overlaps this workflow's watched folder");
  expect(onClose).not.toHaveBeenCalled();
});

it("closes without asking when nothing was changed", () => {
  open();

  fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

  expect(onClose).toHaveBeenCalledTimes(1);
});

it("asks before closing with edits that were not saved", () => {
  open();
  fireEvent.change(screen.getByLabelText("Folder to add"), {
    target: { value: "E:\\More Movies" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Add folder" }));

  fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
  expect(screen.getByTestId("settings-unsaved-changes")).toHaveTextContent(
    "You have unsaved changes to the setup of Movies. Leave without saving?",
  );
  expect(onClose).not.toHaveBeenCalled();

  fireEvent.click(screen.getByTestId("settings-unsaved-changes-confirm"));
  expect(onClose).toHaveBeenCalledTimes(1);
});

it("lets a viewer see the setup but not change it", () => {
  open("viewer");

  expect(screen.queryByLabelText("Folder to add")).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /^Remove/ })).toBeNull();
  expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
  expect(screen.getByTestId("library-rules-profile")).toBeDisabled();
});
