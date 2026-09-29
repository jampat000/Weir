import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";

import * as api from "../../../../lib/processing/libraries-api";
import * as operatorApi from "../../../../lib/processing/operator-settings-api";
import type { ProcessingOperatorSettingsOut } from "../../../../lib/processing/types";
import { LibrariesTab } from "./libraries-tab";
import { asOperator, library, wrapper } from "./library-test-fixtures";

afterEach(() => {
  vi.restoreAllMocks();
});

const WAIT = "Wait after a file last changes (seconds)";
const SIZE = "Minimum file size (MB)";

/** An operator whose Performance settings are these; it must come before any stub the test adds. */
function performanceIs(minFileAgeSeconds: number, minInputFileSizeMb: number) {
  asOperator();
  vi.spyOn(operatorApi, "fetchProcessingOperatorSettings").mockResolvedValue({
    min_file_age_seconds: minFileAgeSeconds,
    min_input_file_size_mb: minInputFileSizeMb,
  } as ProcessingOperatorSettingsOut);
}

async function openEditor(saved = library()) {
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue([saved]);
  render(<LibrariesTab />, { wrapper });
  fireEvent.click(await screen.findByRole("button", { name: "Edit" }));
}

it("says a library uses the Performance setting, and what that setting is now", async () => {
  performanceIs(10, 1);

  await openEditor();

  expect(
    await screen.findAllByText("Uses the Performance setting: 10 seconds"),
  ).toHaveLength(1);
  expect(
    screen.getByText("Uses the Performance setting: 1 MB"),
  ).toBeInTheDocument();
  expect(screen.queryByLabelText(WAIT)).not.toBeInTheDocument();
});

it("starts a value of its own from the Performance setting, and saves it", async () => {
  performanceIs(10, 1);
  const update = vi.spyOn(api, "updateProcessingLibrary");
  await openEditor();
  update.mockResolvedValue(library());
  await screen.findByText("Uses the Performance setting: 10 seconds");

  fireEvent.click(
    screen.getAllByRole("button", { name: "Set for this workflow" })[1],
  );
  const wait = screen.getByLabelText(WAIT);
  expect(wait).toHaveValue("10");
  fireEvent.change(wait, { target: { value: "45" } });
  fireEvent.click(screen.getByTestId("processing-library-save"));

  await waitFor(() =>
    expect(update).toHaveBeenCalledWith(
      1,
      expect.objectContaining({
        min_file_age_seconds: 45,
        min_file_size_mb: null,
      }),
    ),
  );
});

it("shows a library's own value with a way back to the Performance setting", async () => {
  performanceIs(10, 1);
  const update = vi.spyOn(api, "updateProcessingLibrary");
  await openEditor(
    library({
      min_file_size_mb: 200,
      effective_min_file_size_mb: 200,
      min_file_age_seconds: 5,
      effective_min_file_age_seconds: 5,
    }),
  );
  update.mockResolvedValue(library());

  expect(await screen.findByLabelText(SIZE)).toHaveValue("200");
  expect(screen.getByLabelText(WAIT)).toHaveValue("5");
  fireEvent.click(
    await screen.findByRole("button", {
      name: "Use the Performance setting instead (10 seconds)",
    }),
  );
  expect(
    screen.getByText("Uses the Performance setting: 10 seconds"),
  ).toBeInTheDocument();
  fireEvent.click(screen.getByTestId("processing-library-save"));

  await waitFor(() =>
    expect(update).toHaveBeenCalledWith(
      1,
      expect.objectContaining({
        min_file_age_seconds: null,
        min_file_size_mb: 200,
      }),
    ),
  );
});

it("starts a new library on the Performance setting", async () => {
  performanceIs(60, 50);
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
  expect(
    await screen.findByText("Uses the Performance setting: 50 MB"),
  ).toBeInTheDocument();
  fireEvent.click(screen.getByTestId("processing-library-save"));

  await waitFor(() =>
    expect(create).toHaveBeenCalledWith(
      expect.objectContaining({
        name: "Kids",
        min_file_age_seconds: null,
        min_file_size_mb: null,
      }),
    ),
  );
});

it("names the wait after a change and the size check as two different things", async () => {
  performanceIs(60, 50);

  await openEditor();

  expect(await screen.findByText(WAIT)).toBeInTheDocument();
  expect(
    screen.getByLabelText("Wait for the size to stop growing (seconds)"),
  ).toHaveValue("30");
});
