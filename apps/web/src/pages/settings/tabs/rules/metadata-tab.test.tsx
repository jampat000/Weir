import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, expect, it, vi } from "vitest";

import * as authQueries from "../../../../lib/auth/queries";
import * as providerApi from "../../../../lib/processing/metadata-provider-api";
import { MetadataTab } from "./metadata-tab";

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

afterEach(() => {
  vi.restoreAllMocks();
});

function stubProvider() {
  vi.spyOn(authQueries, "useMeQuery").mockReturnValue({
    data: { role: "operator" },
  } as ReturnType<typeof authQueries.useMeQuery>);
  vi.spyOn(providerApi, "fetchProcessingMetadataProvider").mockResolvedValue({
    provider: "",
    base_url: "https://api.themoviedb.org/3",
    key_configured: false,
    known_providers: ["tmdb"],
    artwork_enabled: true,
  });
}

it("offers the metadata provider and the artwork switch, each under its own save note", async () => {
  stubProvider();

  render(<MetadataTab />, { wrapper });

  expect(
    await screen.findByRole("heading", { name: "Metadata provider" }),
  ).toBeInTheDocument();
  expect(screen.getByRole("heading", { name: "Artwork" })).toBeInTheDocument();
  expect(
    screen
      .getAllByTestId("settings-save-model")
      .map((note) => note.textContent),
  ).toEqual([
    "Nothing changes until you press Save.",
    "Changes save as soon as you make them.",
  ]);
});

it("saves the metadata provider and reports whether it answered", async () => {
  stubProvider();
  const saveProvider = vi
    .spyOn(providerApi, "putProcessingMetadataProvider")
    .mockResolvedValue({
      provider: "tmdb",
      base_url: "https://api.themoviedb.org/3",
      key_configured: true,
      known_providers: ["tmdb"],
      artwork_enabled: true,
    });
  const testProvider = vi
    .spyOn(providerApi, "testProcessingMetadataProvider")
    .mockResolvedValue({
      status: "matched",
      detail: "TMDb answered successfully.",
    });

  render(<MetadataTab />, { wrapper });

  fireEvent.click(await screen.findByRole("button", { name: "Configure →" }));
  fireEvent.change(screen.getByRole("combobox", { name: "Provider" }), {
    target: { value: "tmdb" },
  });
  fireEvent.change(screen.getByLabelText("API key"), {
    target: { value: "secret" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save and test" }));

  await waitFor(() => {
    expect(saveProvider).toHaveBeenCalledWith(
      expect.objectContaining({ provider: "tmdb", api_key: "secret" }),
    );
    expect(testProvider).toHaveBeenCalled();
    expect(screen.getByText("TMDb answered successfully.")).toBeInTheDocument();
  });
});

it("keeps a provider address that is typed but not saved when the artwork switch is changed", async () => {
  stubProvider();
  const saveArtwork = vi
    .spyOn(providerApi, "putProcessingMetadataProvider")
    .mockResolvedValue({
      provider: "",
      base_url: "https://api.themoviedb.org/3",
      key_configured: false,
      known_providers: ["tmdb"],
      artwork_enabled: false,
    });

  render(<MetadataTab />, { wrapper });

  fireEvent.click(await screen.findByRole("button", { name: "Configure →" }));
  fireEvent.change(screen.getByRole("combobox", { name: "Provider" }), {
    target: { value: "tmdb" },
  });
  fireEvent.change(screen.getByLabelText("Provider or gateway URL"), {
    target: { value: "https://tmdb.example/3" },
  });
  fireEvent.click(screen.getByRole("radio", { name: "Off" }));

  await waitFor(() => expect(saveArtwork).toHaveBeenCalled());
  await waitFor(() =>
    expect(screen.getByRole("radio", { name: "Off" })).toBeChecked(),
  );
  expect(screen.getByLabelText("Provider or gateway URL")).toHaveValue(
    "https://tmdb.example/3",
  );
  expect(screen.getByRole("combobox", { name: "Provider" })).toHaveValue(
    "tmdb",
  );
});

it("shows a load error when the metadata settings fail to load", async () => {
  vi.spyOn(authQueries, "useMeQuery").mockReturnValue({
    data: { role: "operator" },
  } as ReturnType<typeof authQueries.useMeQuery>);
  vi.spyOn(providerApi, "fetchProcessingMetadataProvider").mockRejectedValue(
    new Error("network down"),
  );

  render(<MetadataTab />, { wrapper });

  expect(await screen.findByTestId("settings-load-error")).toHaveTextContent(
    "Weir couldn’t load your metadata settings.",
  );
});
