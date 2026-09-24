import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import * as api from "../../../../lib/media-managers/media-managers-api";
import type { MediaManagerConnection } from "../../../../lib/media-managers/media-managers-api";
import { ConnectionEditForm } from "./connection-edit-form";

function connection(
  over: Partial<MediaManagerConnection> = {},
): MediaManagerConnection {
  return {
    id: 1,
    kind: "deluno",
    name: "Deluno",
    enabled: true,
    base_url: "http://192.0.2.10:5099",
    api_key_is_saved: true,
    webhook_secret_is_set: false,
    webhook_url_path: "/api/v1/intake/webhook/deluno",
    unsigned_webhook_warning: null,
    last_test_ok: null,
    last_test_at: null,
    last_test_detail: null,
    lanes: [],
    ...over,
  };
}

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe("ConnectionEditForm", () => {
  it("saves the name and address without touching the api key when it is left blank", async () => {
    const update = vi
      .spyOn(api, "updateMediaManagerConnection")
      .mockResolvedValue(connection({ name: "Deluno 2" }));
    const onClose = vi.fn();

    render(<ConnectionEditForm connection={connection()} onClose={onClose} />, {
      wrapper,
    });

    fireEvent.change(screen.getByTestId("media-manager-edit-name"), {
      target: { value: "Deluno 2" },
    });
    fireEvent.click(screen.getByTestId("media-manager-edit-save"));

    await waitFor(() => expect(update).toHaveBeenCalledTimes(1));
    expect(update).toHaveBeenCalledWith(1, {
      name: "Deluno 2",
      base_url: "http://192.0.2.10:5099",
    });
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
  });

  it("sends the typed api key only when something was typed", async () => {
    const update = vi
      .spyOn(api, "updateMediaManagerConnection")
      .mockResolvedValue(connection());

    render(<ConnectionEditForm connection={connection()} onClose={vi.fn()} />, {
      wrapper,
    });

    fireEvent.change(screen.getByTestId("media-manager-edit-api-key"), {
      target: { value: "new-key" },
    });
    fireEvent.click(screen.getByTestId("media-manager-edit-save"));

    await waitFor(() => expect(update).toHaveBeenCalledTimes(1));
    expect(update).toHaveBeenCalledWith(1, {
      name: "Deluno",
      base_url: "http://192.0.2.10:5099",
      api_key: "new-key",
    });
  });

  it("shows why the save failed instead of closing the form", async () => {
    vi.spyOn(api, "updateMediaManagerConnection").mockRejectedValue(
      new Error("Could not reach the server."),
    );
    const onClose = vi.fn();

    render(<ConnectionEditForm connection={connection()} onClose={onClose} />, {
      wrapper,
    });

    fireEvent.change(screen.getByTestId("media-manager-edit-name"), {
      target: { value: "Deluno 2" },
    });
    fireEvent.click(screen.getByTestId("media-manager-edit-save"));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Could not reach the server.",
    );
    expect(onClose).not.toHaveBeenCalled();
  });

  it("closes at once when Cancel is pressed with nothing changed", () => {
    const onClose = vi.fn();
    render(<ConnectionEditForm connection={connection()} onClose={onClose} />, {
      wrapper,
    });

    fireEvent.click(screen.getByTestId("media-manager-edit-cancel"));

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(
      screen.queryByTestId("settings-unsaved-changes"),
    ).not.toBeInTheDocument();
  });

  it("asks before dropping an edited field", async () => {
    const onClose = vi.fn();
    render(<ConnectionEditForm connection={connection()} onClose={onClose} />, {
      wrapper,
    });

    fireEvent.change(screen.getByTestId("media-manager-edit-name"), {
      target: { value: "Deluno 2" },
    });
    fireEvent.click(screen.getByTestId("media-manager-edit-cancel"));

    const dialog = await screen.findByTestId("settings-unsaved-changes");
    expect(dialog).toHaveTextContent("Deluno");
    expect(onClose).not.toHaveBeenCalled();

    fireEvent.click(screen.getByTestId("settings-unsaved-changes-confirm"));
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
  });
});
