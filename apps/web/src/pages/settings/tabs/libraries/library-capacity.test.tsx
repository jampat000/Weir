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

const MOST_AT_ONCE = /Most files at once from this workflow/;
const KEEP_FREE =
  /Keep at least this many GB free on the drive this workflow writes to/;

function performance(
  filesAtOnce: number,
): Pick<ProcessingOperatorSettingsOut, "max_concurrent_files"> {
  return { max_concurrent_files: filesAtOnce };
}

/** Performance's total for the editor, which reads only "Files at once" from it. */
function weirRunsAtOnce(filesAtOnce: number) {
  vi.spyOn(operatorApi, "fetchProcessingOperatorSettings").mockResolvedValue(
    performance(filesAtOnce) as ProcessingOperatorSettingsOut,
  );
}

async function openEditor(saved: api.ProcessingLibrary) {
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue([saved]);
  render(<LibrariesTab />, { wrapper });
  fireEvent.click(await screen.findByRole("button", { name: "Edit" }));
  return screen.findByRole("textbox", { name: MOST_AT_ONCE });
}

it("shows a workflow with no limit of its own as blank, against Weir's total", async () => {
  asOperator();
  weirRunsAtOnce(4);

  const field = await openEditor(library({ max_concurrent_files: 0 }));

  expect(field).toHaveValue("");
  expect(
    await screen.findByText(
      "No limit of its own: it can use any of Weir's 4 files at once, alongside the other workflows.",
    ),
  ).toBeInTheDocument();
});

it("shows a limit as a share of Weir's total and saves it", async () => {
  asOperator();
  weirRunsAtOnce(4);
  const saved = library({ max_concurrent_files: 0 });
  const update = vi
    .spyOn(api, "updateProcessingLibrary")
    .mockResolvedValue(saved);
  const field = await openEditor(saved);

  fireEvent.change(field, { target: { value: "3" } });

  expect(
    await screen.findByText("Up to 3 of Weir's 4 files at once."),
  ).toBeInTheDocument();
  fireEvent.click(screen.getByTestId("processing-library-save"));
  await waitFor(() =>
    expect(update).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ max_concurrent_files: 3 }),
    ),
  );
});

it("saves a cleared limit as no limit of its own", async () => {
  asOperator();
  weirRunsAtOnce(4);
  const saved = library({ max_concurrent_files: 2 });
  const update = vi
    .spyOn(api, "updateProcessingLibrary")
    .mockResolvedValue(saved);
  const field = await openEditor(saved);
  expect(field).toHaveValue("2");

  fireEvent.change(field, { target: { value: "" } });
  fireEvent.click(screen.getByTestId("processing-library-save"));

  await waitFor(() =>
    expect(update).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ max_concurrent_files: 0 }),
    ),
  );
});

it("tells a workflow that Performance was lowered under that it is limited by the total", async () => {
  asOperator();
  weirRunsAtOnce(2);

  await openEditor(library({ max_concurrent_files: 3 }));

  expect(
    await screen.findByTestId("workflow-limited-by-total"),
  ).toHaveTextContent(
    "Weir runs 2 files at once in total, so this workflow is limited to 2. Choose 2 or fewer, or raise Files at once in Setup › Performance › Speed.",
  );
});

it("shows the server's refusal when a limit above the total is saved", async () => {
  asOperator();
  weirRunsAtOnce(4);
  const saved = library({ max_concurrent_files: 0 });
  vi.spyOn(api, "updateProcessingLibrary").mockRejectedValue(
    new Error(
      "The most files this workflow runs at once cannot be more than the 4 Weir runs in total. Choose a lower number, or raise Files at once in Settings › Performance first.",
    ),
  );
  const field = await openEditor(saved);

  fireEvent.change(field, { target: { value: "6" } });
  fireEvent.click(screen.getByTestId("processing-library-save"));

  expect(
    await screen.findByTestId("processing-library-save-error"),
  ).toHaveTextContent("cannot be more than the 4 Weir runs in total");
});

it("shows the space a workflow keeps free in gigabytes and saves what is typed", async () => {
  asOperator();
  const saved = library({ minimum_free_disk_space_mb: 20480 });
  const update = vi
    .spyOn(api, "updateProcessingLibrary")
    .mockResolvedValue(saved);
  await openEditor(saved);
  const field = screen.getByRole("textbox", { name: KEEP_FREE });
  expect(field).toHaveValue("20");

  fireEvent.change(field, { target: { value: "12.5" } });
  fireEvent.click(screen.getByTestId("processing-library-save"));

  await waitFor(() =>
    expect(update).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ minimum_free_disk_space_mb: 12800 }),
    ),
  );
});

it("keeps 5 GB free by default in a new workflow, and 0 turns the check off", async () => {
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
  const field = screen.getByRole("textbox", { name: KEEP_FREE });
  expect(field).toHaveValue("5");
  fireEvent.change(field, { target: { value: "0" } });
  fireEvent.click(screen.getByTestId("processing-library-save"));

  await waitFor(() =>
    expect(create).toHaveBeenCalledWith(
      expect.objectContaining({ name: "Kids", minimum_free_disk_space_mb: 0 }),
    ),
  );
});
