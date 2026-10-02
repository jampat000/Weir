import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, expect, it, vi } from "vitest";

import * as processingQueries from "../../lib/processing/queries";
import { HistoryRetentionNote } from "./history-retention-note";

function stubDays(days: number | null) {
  vi.spyOn(
    processingQueries,
    "useProcessingOperatorSettingsQuery",
  ).mockReturnValue({
    data: days === null ? undefined : { file_log_retention_days: days },
  } as unknown as ReturnType<
    typeof processingQueries.useProcessingOperatorSettingsQuery
  >);
}

function renderNote() {
  return render(
    <MemoryRouter>
      <HistoryRetentionNote />
    </MemoryRouter>,
  );
}

afterEach(() => vi.restoreAllMocks());

it("says how long a file's history is kept, and links to where it is changed", () => {
  stubDays(30);

  renderNote();

  expect(screen.getByTestId("history-retention")).toHaveTextContent(
    "File history is kept 30 days after a file is gone · change",
  );
  expect(screen.getByRole("link", { name: "change" })).toHaveAttribute(
    "href",
    "/system?tab=logs#retention",
  );
});

it("says a file's history is kept until the file is removed when the days are 0", () => {
  stubDays(0);

  renderNote();

  expect(screen.getByTestId("history-retention")).toHaveTextContent(
    "File history is kept until the file is removed · change",
  );
});

it("shows nothing while the setting is still loading", () => {
  stubDays(null);

  const { container } = renderNote();

  expect(container).toBeEmptyDOMElement();
});
