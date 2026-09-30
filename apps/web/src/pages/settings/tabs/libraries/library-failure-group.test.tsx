import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";

import * as api from "../../../../lib/processing/libraries-api";
import { LibrariesTab } from "./libraries-tab";
import { asOperator, library, wrapper } from "./library-test-fixtures";

afterEach(() => {
  vi.restoreAllMocks();
});

async function openEditor() {
  asOperator();
  const existing = library({ max_attempts: 4, retry_backoff_seconds: 120 });
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue([existing]);
  render(<LibrariesTab />, { wrapper });
  fireEvent.click(await screen.findByRole("button", { name: "Edit" }));
  return existing;
}

it("keeps everything about a failed file in one When a file fails section", async () => {
  await openEditor();

  const failure = screen.getByRole("region", { name: "When a file fails" });
  expect(
    within(failure).getByLabelText(/Maximum automatic attempts/),
  ).toHaveValue("4");
  expect(within(failure).getByLabelText(/First retry delay/)).toHaveValue(
    "120",
  );
  expect(
    within(failure).getByLabelText("Retry processing failures"),
  ).toBeInTheDocument();
  expect(
    within(failure).getByLabelText(/Retry files that failed the pre-check/),
  ).toBeInTheDocument();
  expect(
    within(failure).getByRole("combobox", { name: /When retries run out/ }),
  ).toBeInTheDocument();
});

it("keeps the retry settings out of the capacity section", async () => {
  await openEditor();

  const capacity = screen.getByRole("region", { name: "Capacity" });
  expect(
    within(capacity).queryByLabelText(/Maximum automatic attempts/),
  ).not.toBeInTheDocument();
  expect(
    within(capacity).queryByLabelText(/First retry delay/),
  ).not.toBeInTheDocument();
  expect(
    within(capacity).queryByRole("combobox", { name: /When retries run out/ }),
  ).not.toBeInTheDocument();
});

it("says that the first try counts and that Weir looks again shortly after the delay", async () => {
  await openEditor();

  const failure = screen.getByRole("region", { name: "When a file fails" });
  expect(failure).toHaveTextContent(
    "The first try counts: 3 means the first try and two retries.",
  );
  expect(failure).toHaveTextContent(
    "Weir looks again shortly after this wait.",
  );
  expect(failure).toHaveTextContent(
    "A file it gives up on shows as Failed in History, with the reason.",
  );
});

it("saves a changed attempt limit with the workflow", async () => {
  const existing = await openEditor();
  const update = vi
    .spyOn(api, "updateProcessingLibrary")
    .mockResolvedValue(existing);

  fireEvent.change(screen.getByLabelText(/Maximum automatic attempts/), {
    target: { value: "6" },
  });
  fireEvent.click(screen.getByTestId("processing-library-save"));

  await waitFor(() =>
    expect(update).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ max_attempts: 6, retry_backoff_seconds: 120 }),
    ),
  );
});
