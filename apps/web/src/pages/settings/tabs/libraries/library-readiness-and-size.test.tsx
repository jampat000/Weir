import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";

import * as api from "../../../../lib/processing/libraries-api";
import { LibrariesTab } from "./libraries-tab";
import { asOperator, library, wrapper } from "./library-test-fixtures";

afterEach(() => {
  vi.restoreAllMocks();
});

const READY = "A new file is ready once it hasn't changed for (seconds)";
const SIZE = "Minimum file size (MB)";

async function openEditor(saved = library()) {
  asOperator();
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue([saved]);
  render(<LibrariesTab />, { wrapper });
  fireEvent.click(await screen.findByRole("button", { name: "Edit" }));
}

it("shows the workflow's own wait and minimum size as plain numbers", async () => {
  await openEditor(library({ ready_after_seconds: 45, min_file_size_mb: 200 }));

  expect(await screen.findByLabelText(READY)).toHaveValue("45");
  expect(screen.getByLabelText(SIZE)).toHaveValue("200");
  expect(screen.queryByText(/Uses the Performance setting/)).toBeNull();
});

it("saves a changed wait and minimum size on the workflow", async () => {
  const update = vi.spyOn(api, "updateProcessingLibrary");
  await openEditor();
  update.mockResolvedValue(library());

  fireEvent.change(await screen.findByLabelText(READY), {
    target: { value: "90" },
  });
  fireEvent.change(screen.getByLabelText(SIZE), { target: { value: "0" } });
  fireEvent.click(screen.getByTestId("processing-library-save"));

  await waitFor(() =>
    expect(update).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ ready_after_seconds: 90, min_file_size_mb: 0 }),
    ),
  );
});

it("starts a new workflow at 60 seconds and 50 MB, filled in, and creates it with them", async () => {
  asOperator();
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue([library()]);
  const create = vi
    .spyOn(api, "createProcessingLibrary")
    .mockResolvedValue(library({ id: 9, name: "Kids" }));

  render(<LibrariesTab />, { wrapper });
  fireEvent.click(await screen.findByTestId("processing-library-add"));
  fireEvent.click(await screen.findByTestId("add-workflow-continue"));
  fireEvent.change(screen.getByPlaceholderText("Movies 4K"), {
    target: { value: "Kids" },
  });

  expect(await screen.findByLabelText(READY)).toHaveValue("60");
  expect(screen.getByLabelText(SIZE)).toHaveValue("50");
  fireEvent.click(screen.getByTestId("processing-library-save"));

  await waitFor(() =>
    expect(create).toHaveBeenCalledWith(
      expect.objectContaining({
        name: "Kids",
        ready_after_seconds: 60,
        min_file_size_mb: 50,
      }),
    ),
  );
});

it("has one wait, not the three it replaced", async () => {
  await openEditor();

  await screen.findByLabelText(READY);
  expect(screen.queryByText(/Wait after a file last changes/)).toBeNull();
  expect(screen.queryByText(/Hold every new file/)).toBeNull();
  expect(screen.queryByText(/Wait for the size to stop growing/)).toBeNull();
});

it("keeps the other readiness settings as they were", async () => {
  await openEditor();

  await screen.findByLabelText(READY);
  expect(
    screen.getByLabelText("Look for new files every (seconds)"),
  ).toBeInTheDocument();
  expect(screen.getByText("Watch this folder for changes")).toBeInTheDocument();
  expect(screen.getByText("Ignore size changes")).toBeInTheDocument();
  expect(screen.getByText("Skip read and write tests")).toBeInTheDocument();
});

it("no longer says a minimum size or wait can come from Performance", async () => {
  await openEditor();

  await screen.findByLabelText(SIZE);
  expect(screen.queryByText(/Performance setting/)).toBeNull();
  expect(screen.queryByText(/never deletes anything/)).toBeNull();
});
