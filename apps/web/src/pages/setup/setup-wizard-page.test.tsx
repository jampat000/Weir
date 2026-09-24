import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { authKeys } from "../../lib/auth/query-keys";
import type { ProcessingLibrary } from "../../lib/processing/libraries-api";
import { settingsKeys } from "../../lib/settings/query-keys";
import { SetupWizardPage } from "./setup-wizard-page";

const {
  navigateMock,
  settingsMutateAsyncMock,
  createLibraryMock,
  updateLibraryMock,
  librariesState,
} = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  settingsMutateAsyncMock: vi.fn(),
  createLibraryMock: vi.fn(),
  updateLibraryMock: vi.fn(),
  librariesState: { data: [] as ProcessingLibrary[] },
}));

function existingLibrary(over: Partial<ProcessingLibrary>): ProcessingLibrary {
  return {
    id: 7,
    name: "Films",
    enabled: true,
    media_type: "movie",
    display_order: 0,
    watched_folder: "C:\\Old\\Movies",
    work_folder: "C:\\Work",
    output_folder: "C:\\Old\\MoviesOut",
    scan_interval_seconds: 900,
    rule_set_id: 3,
    manager_connection_ids: [2],
    ...over,
  } as ProcessingLibrary;
}

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return {
    ...actual,
    useNavigate: () => navigateMock,
  };
});

vi.mock("../../lib/settings/queries", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../lib/settings/queries")>();
  return {
    ...actual,
    useAppSettingsSaveMutation: () => ({
      isPending: false,
      mutateAsync: settingsMutateAsyncMock,
    }),
  };
});

vi.mock("../../lib/processing/libraries-queries", () => ({
  useProcessingLibrariesQuery: () => ({
    isPending: false,
    data: librariesState.data,
  }),
  useCreateProcessingLibrary: () => ({
    isPending: false,
    mutateAsync: createLibraryMock,
  }),
  useUpdateProcessingLibrary: () => ({
    isPending: false,
    mutateAsync: updateLibraryMock,
  }),
}));

vi.mock("../../lib/api/auth-api", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../lib/api/auth-api")>();
  return {
    ...actual,
    fetchCsrfToken: vi.fn().mockResolvedValue("csrf-token"),
  };
});

function wrap(ui: ReactNode, client: QueryClient) {
  return (
    <QueryClientProvider client={client}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>
  );
}

function renderWizard() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Infinity } },
  });
  client.setQueryData(authKeys.me, { id: 1, username: "admin", role: "admin" });
  client.setQueryData(settingsKeys.app, {
    product_display_name: "Weir",
    signed_in_home_notice: null,
    setup_wizard_state: "pending",
    app_timezone: "UTC",
    log_retention_days: 30,
    configuration_backup_enabled: false,
    configuration_backup_interval_hours: 24,
    configuration_backup_preferred_time: "02:00",
    configuration_backup_last_run_at: null,
    updated_at: "2026-04-23T00:00:00Z",
  });
  return render(wrap(<SetupWizardPage />, client));
}

describe("SetupWizardPage", () => {
  beforeEach(() => {
    navigateMock.mockReset();
    settingsMutateAsyncMock.mockReset();
    createLibraryMock.mockReset();
    updateLibraryMock.mockReset();
    librariesState.data = [];
    settingsMutateAsyncMock.mockResolvedValue({});
    createLibraryMock.mockResolvedValue({});
    updateLibraryMock.mockResolvedValue({});
  });

  it("ends on What's next, with a way into Weir and no start page to choose (#459)", async () => {
    renderWizard();

    expect(
      screen.queryByRole("radiogroup", { name: "Open first" }),
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Finish setup" }));

    await waitFor(() => {
      expect(
        screen.getByRole("heading", { name: "What's next" }),
      ).toBeInTheDocument();
    });
    expect(
      screen.getByRole("link", { name: "Choose which tracks to keep" }),
    ).toHaveAttribute("href", "/settings?tab=rules");
    expect(
      screen.getByRole("link", { name: "Connect Sonarr, Radarr or Deluno" }),
    ).toHaveAttribute("href", "/settings?tab=media-managers");
    expect(
      screen.getByRole("link", { name: "Clean files you already have" }),
    ).toHaveAttribute("href", "/library");
    expect(
      screen.getByRole("link", { name: "Continue to Weir" }),
    ).toHaveAttribute("href", "/");
  });

  it("skips without saving the edited draft, only the wizard's own state", async () => {
    renderWizard();

    fireEvent.change(screen.getByDisplayValue("02:00"), {
      target: { value: "03:30" },
    });
    fireEvent.change(
      screen.getByRole("textbox", { name: "Movies watched folder" }),
      { target: { value: "D:\\Movies" } },
    );
    fireEvent.click(screen.getByTestId("setup-wizard-skip"));

    await waitFor(() => {
      expect(settingsMutateAsyncMock).toHaveBeenCalledWith(
        expect.objectContaining({
          setup_wizard_state: "skipped",
          // The edited time and folder are dropped: skipping never saves what was typed.
          app_timezone: "UTC",
          configuration_backup_preferred_time: "02:00",
        }),
      );
    });
    expect(createLibraryMock).not.toHaveBeenCalled();
    await waitFor(() => {
      expect(navigateMock).toHaveBeenCalledWith("/", { replace: true });
    });
  });

  it("surfaces a failed skip instead of leaving silently", async () => {
    settingsMutateAsyncMock.mockRejectedValueOnce(new Error("boom"));
    renderWizard();

    fireEvent.click(screen.getByTestId("setup-wizard-skip"));

    await waitFor(() => {
      expect(screen.getByRole("alert")).toHaveTextContent("boom");
    });
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("completes and saves backup plus library starter settings", async () => {
    renderWizard();

    fireEvent.change(screen.getByDisplayValue("02:00"), {
      target: { value: "03:30" },
    });
    fireEvent.change(
      screen.getByRole("textbox", { name: "Movies watched folder" }),
      {
        target: { value: "D:\\Movies" },
      },
    );
    fireEvent.change(
      screen.getByRole("textbox", { name: "Movies output folder" }),
      {
        target: { value: "E:\\MoviesOut" },
      },
    );
    fireEvent.click(screen.getByRole("button", { name: "Finish setup" }));

    await waitFor(() => {
      expect(settingsMutateAsyncMock).toHaveBeenCalledWith(
        expect.objectContaining({
          setup_wizard_state: "completed",
          configuration_backup_preferred_time: "03:30",
        }),
      );
    });
    expect(createLibraryMock).toHaveBeenCalledTimes(1);
    expect(createLibraryMock).toHaveBeenCalledWith({
      name: "Movies",
      media_type: "movie",
      watched_folder: "D:\\Movies",
      output_folder: "E:\\MoviesOut",
    });
    expect(updateLibraryMock).not.toHaveBeenCalled();
    await waitFor(() => {
      expect(
        screen.getByRole("heading", { name: "What's next" }),
      ).toBeInTheDocument();
    });
  });

  it("does not mark the wizard complete when a library save fails first", async () => {
    createLibraryMock.mockRejectedValueOnce(new Error("boom"));
    renderWizard();

    fireEvent.change(
      screen.getByRole("textbox", { name: "Movies watched folder" }),
      { target: { value: "D:\\Movies" } },
    );
    fireEvent.change(
      screen.getByRole("textbox", { name: "Movies output folder" }),
      { target: { value: "E:\\MoviesOut" } },
    );
    fireEvent.click(screen.getByRole("button", { name: "Finish setup" }));

    await waitFor(() => {
      expect(screen.getByRole("alert")).toBeInTheDocument();
    });
    expect(settingsMutateAsyncMock).not.toHaveBeenCalled();
    expect(
      screen.queryByRole("heading", { name: "What's next" }),
    ).not.toBeInTheDocument();
  });

  it("creates a library for each media type that has folders and none yet", async () => {
    renderWizard();

    fireEvent.change(
      screen.getByRole("textbox", { name: "TV watched folder" }),
      {
        target: { value: "D:\\TV" },
      },
    );
    fireEvent.change(
      screen.getByRole("textbox", { name: "TV output folder" }),
      {
        target: { value: "E:\\TVOut" },
      },
    );
    fireEvent.change(
      screen.getByRole("textbox", { name: "Movies watched folder" }),
      {
        target: { value: "D:\\Movies" },
      },
    );
    fireEvent.change(
      screen.getByRole("textbox", { name: "Movies output folder" }),
      {
        target: { value: "E:\\MoviesOut" },
      },
    );
    fireEvent.click(screen.getByRole("button", { name: "Finish setup" }));

    await waitFor(() => {
      expect(createLibraryMock).toHaveBeenCalledTimes(2);
    });
    expect(createLibraryMock).toHaveBeenCalledWith(
      expect.objectContaining({ name: "TV", media_type: "tv" }),
    );
    expect(createLibraryMock).toHaveBeenCalledWith(
      expect.objectContaining({ name: "Movies", media_type: "movie" }),
    );
    expect(updateLibraryMock).not.toHaveBeenCalled();
  });

  it("updates the first existing library of a media type instead of adding another", async () => {
    librariesState.data = [
      existingLibrary({ id: 9, name: "4K films", display_order: 1 }),
      existingLibrary({ id: 7, name: "Films", display_order: 0 }),
    ];
    renderWizard();

    const watched = screen.getByRole("textbox", {
      name: "Movies watched folder",
    });
    expect(watched).toHaveValue("C:\\Old\\Movies");
    fireEvent.change(watched, { target: { value: "D:\\Movies" } });
    fireEvent.click(screen.getByRole("button", { name: "Finish setup" }));

    await waitFor(() => {
      expect(updateLibraryMock).toHaveBeenCalledTimes(1);
    });
    expect(updateLibraryMock).toHaveBeenCalledWith({
      id: 7,
      data: expect.objectContaining({
        name: "Films",
        media_type: "movie",
        watched_folder: "D:\\Movies",
        output_folder: "C:\\Old\\MoviesOut",
        work_folder: "C:\\Work",
        scan_interval_seconds: 900,
        rule_set_id: 3,
        manager_connection_ids: [2],
      }),
    });
    expect(createLibraryMock).not.toHaveBeenCalled();
  });
});
