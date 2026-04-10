import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { RefinerPage } from "./refiner-page";

describe("RefinerPage (product surface)", () => {
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

  it("leads with refining movies/TV, not Fetcher boundary essays", () => {
    render(
      <MemoryRouter>
        <RefinerPage />
      </MemoryRouter>,
    );
    const main = screen.getByTestId("refiner-scope-page");
    expect(main.textContent).toMatch(/Refin/i);
    expect(main.textContent).toMatch(/movies|TV/i);
    expect(main.textContent).not.toMatch(/surface area|module home/i);
  });

  it("keeps a single short pointer to Fetcher", () => {
    render(
      <MemoryRouter>
        <RefinerPage />
      </MemoryRouter>,
    );
    const links = screen.getAllByRole("link", { name: "Fetcher" });
    expect(links).toHaveLength(1);
    expect(links[0]).toHaveAttribute("href", "/app/fetcher");
    const lead = screen.getByText(/covers download queues/i);
    expect(lead.textContent!.length).toBeLessThan(120);
  });
});
