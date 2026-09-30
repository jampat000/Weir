import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, expect, it, vi } from "vitest";

import * as processingQueries from "../../lib/processing/queries";
import { HistoryRetentionSetting } from "./history-retention";

const mutate = vi.fn();

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

function setup(
  days: number | null,
  options: { isError?: boolean; isPending?: boolean; saveError?: Error } = {},
) {
  vi.spyOn(
    processingQueries,
    "useProcessingOperatorSettingsQuery",
  ).mockReturnValue({
    data: days === null ? undefined : { file_log_retention_days: days },
    isError: options.isError ?? false,
  } as unknown as ReturnType<
    typeof processingQueries.useProcessingOperatorSettingsQuery
  >);
  vi.spyOn(
    processingQueries,
    "useProcessingOperatorSettingsSaveMutation",
  ).mockReturnValue({
    mutate,
    isPending: options.isPending ?? false,
    isError: options.saveError !== undefined,
    error: options.saveError ?? null,
  } as unknown as ReturnType<
    typeof processingQueries.useProcessingOperatorSettingsSaveMutation
  >);
}

afterEach(() => {
  vi.restoreAllMocks();
  mutate.mockReset();
});

it("shows today's number of days beside the records it governs", () => {
  setup(90);

  render(<HistoryRetentionSetting editable />, { wrapper });

  expect(screen.getByLabelText("Keep a file’s history for")).toHaveValue(90);
  expect(screen.getByText("days after it’s gone")).toBeInTheDocument();
  expect(
    screen.getByText(
      "While Weir still knows a file, its history is kept. 0 keeps it for ever.",
    ),
  ).toBeInTheDocument();
});

it("offers Save only once the number has changed, and saves it", async () => {
  setup(90);
  mutate.mockImplementation(
    (_body: unknown, options: { onSuccess: () => void }) => options.onSuccess(),
  );

  render(<HistoryRetentionSetting editable />, { wrapper });
  expect(
    screen.queryByRole("button", { name: "Save" }),
  ).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("Keep a file’s history for"), {
    target: { value: "30" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save" }));

  await waitFor(() =>
    expect(mutate).toHaveBeenCalledWith(
      { file_log_retention_days: 30 },
      expect.anything(),
    ),
  );
  expect(await screen.findByRole("status")).toHaveTextContent(
    "Saved. A file’s history is kept for 30 days after it’s gone.",
  );
});

it("says a file's history is kept until it is removed when the days are set to 0", async () => {
  setup(90);
  mutate.mockImplementation(
    (_body: unknown, options: { onSuccess: () => void }) => options.onSuccess(),
  );

  render(<HistoryRetentionSetting editable />, { wrapper });
  fireEvent.change(screen.getByLabelText("Keep a file’s history for"), {
    target: { value: "0" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save" }));

  expect(await screen.findByRole("status")).toHaveTextContent(
    "Saved. A file’s history is kept until it is removed.",
  );
});

it("refuses a number outside 0 to 3650 and a blank one", () => {
  setup(90);

  render(<HistoryRetentionSetting editable />, { wrapper });
  const input = screen.getByLabelText("Keep a file’s history for");
  fireEvent.change(input, { target: { value: "4000" } });
  expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
  fireEvent.change(input, { target: { value: "" } });
  expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
  fireEvent.change(input, { target: { value: "3650" } });
  expect(screen.getByRole("button", { name: "Save" })).not.toBeDisabled();
});

it("shows Saving… while the change is saved, and a failure in words", () => {
  setup(90, { isPending: true });
  const { unmount } = render(<HistoryRetentionSetting editable />, { wrapper });
  fireEvent.change(screen.getByLabelText("Keep a file’s history for"), {
    target: { value: "30" },
  });
  expect(screen.getByRole("button", { name: "Saving…" })).toBeDisabled();
  unmount();

  vi.restoreAllMocks();
  setup(90, { saveError: new Error("The server said no.") });
  render(<HistoryRetentionSetting editable />, { wrapper });
  expect(screen.getByRole("alert")).toHaveTextContent(
    /could not be saved|The server said no/,
  );
});

it("gives a viewer the sentence to read and no way to change it", () => {
  setup(90);

  render(<HistoryRetentionSetting editable={false} />, { wrapper });

  expect(screen.getByTestId("history-retention")).toHaveTextContent(
    "Weir keeps a file’s history for 90 days after it’s gone.",
  );
  expect(
    screen.queryByLabelText("Keep a file’s history for"),
  ).not.toBeInTheDocument();
});

it("tells a viewer when history is kept until it is removed", () => {
  setup(0);

  render(<HistoryRetentionSetting editable={false} />, { wrapper });

  expect(screen.getByTestId("history-retention")).toHaveTextContent(
    "Weir keeps a file’s history until it is removed.",
  );
});

it("says so when the setting could not be read", () => {
  setup(null, { isError: true });

  render(<HistoryRetentionSetting editable />, { wrapper });

  expect(screen.getByRole("alert")).toHaveTextContent(
    "Weir could not read how long a file’s history is kept.",
  );
});

it("shows nothing while the setting is still loading", () => {
  setup(null);

  const { container } = render(<HistoryRetentionSetting editable />, {
    wrapper,
  });

  expect(container).toBeEmptyDOMElement();
});
