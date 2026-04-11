import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { RefinerPage } from "./refiner-page";

describe("RefinerPage (hero compression)", () => {
  it("does not host Fetcher failed-import UI", () => {
    render(
      <MemoryRouter>
        <RefinerPage />
      </MemoryRouter>,
    );
    expect(screen.queryByTestId("fetcher-failed-imports-workspace")).toBeNull();
    expect(screen.queryByTestId("fetcher-failed-imports-settings")).toBeNull();
    expect(screen.queryByTestId("fetcher-failed-imports-status-filter")).toBeNull();
  });

  it("hero describes Refiner only — no Fetcher, *arr, roadmap, or contrast language", () => {
    const { container } = render(
      <MemoryRouter>
        <RefinerPage />
      </MemoryRouter>,
    );
    const hero = container.querySelector(".mm-page__intro");
    expect(hero).toBeTruthy();
    const t = hero!.textContent ?? "";
    expect(t).toMatch(/Refin/i);
    expect(t).toMatch(/movies|TV/i);
    expect(t).not.toMatch(/Fetcher/i);
    expect(t).not.toMatch(/Radarr|Sonarr/i);
    expect(t).not.toMatch(/\blater\b|separate from|not about|queue/i);
  });

  it("has no Fetcher link on the page", () => {
    render(
      <MemoryRouter>
        <RefinerPage />
      </MemoryRouter>,
    );
    expect(screen.queryByRole("link", { name: "Fetcher" })).toBeNull();
  });
});
