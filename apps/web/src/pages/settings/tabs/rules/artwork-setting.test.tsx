import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";

import * as providerApi from "../../../../lib/processing/metadata-provider-api";
import type { ProcessingMetadataProvider } from "../../../../lib/processing/metadata-provider-api";
import { ArtworkSetting } from "./artwork-setting";

const saved: ProcessingMetadataProvider = {
  provider: "tmdb",
  base_url: "https://api.themoviedb.org/3",
  key_configured: true,
  known_providers: ["tmdb"],
  artwork_enabled: true,
};

function renderSetting(provider: ProcessingMetadataProvider, editable = true) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={client}>
      <ArtworkSetting saved={provider} editable={editable} />
    </QueryClientProvider>,
  );
}

afterEach(() => {
  vi.restoreAllMocks();
});

it("says what artwork sends away and which way it is set", () => {
  renderSetting(saved);

  expect(screen.getByText(/Only the title and year are sent/)).toBeVisible();
  expect(screen.getByRole("radio", { name: "On" })).toBeChecked();
});

it("saves the switch as soon as it is turned off, leaving the provider as it was", async () => {
  const put = vi
    .spyOn(providerApi, "putProcessingMetadataProvider")
    .mockResolvedValue({ ...saved, artwork_enabled: false });
  renderSetting(saved);

  fireEvent.click(screen.getByRole("radio", { name: "Off" }));

  await waitFor(() =>
    expect(put).toHaveBeenCalledWith({
      provider: "tmdb",
      base_url: "https://api.themoviedb.org/3",
      artwork_enabled: false,
    }),
  );
});

it("says so when the choice could not be saved", async () => {
  vi.spyOn(providerApi, "putProcessingMetadataProvider").mockRejectedValue(
    new TypeError("Failed to fetch"),
  );
  renderSetting(saved);

  fireEvent.click(screen.getByRole("radio", { name: "Off" }));

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "The artwork setting could not be saved.",
  );
});

it("cannot be changed by someone who may not edit settings", () => {
  renderSetting({ ...saved, artwork_enabled: false }, false);

  expect(screen.getByRole("radio", { name: "Off" })).toBeChecked();
  expect(screen.getByRole("radio", { name: "On" })).toBeDisabled();
});
