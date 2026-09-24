import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import type { ProcessingLibrary } from "../../lib/processing/libraries-api";
import { LibraryPicker } from "./library-picker";

function library(id: number, name: string): ProcessingLibrary {
  return { id, name, media_type: "tv" } as ProcessingLibrary;
}

describe("LibraryPicker", () => {
  it("shows the one library's name with no control, when there is only one", () => {
    render(
      <LibraryPicker
        libraries={[library(1, "TV")]}
        chosenId={1}
        onPick={vi.fn()}
      />,
    );

    expect(screen.getByTestId("library-picker")).toHaveTextContent("TV");
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("moves through the list with the arrow keys and picks with Enter", () => {
    const onPick = vi.fn();
    render(
      <LibraryPicker
        libraries={[library(1, "TV"), library(2, "Movies"), library(3, "Docs")]}
        chosenId={1}
        onPick={onPick}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: /TV/ }));
    const options = screen.getAllByRole("option");
    expect(document.activeElement).toBe(options[0]);

    fireEvent.keyDown(screen.getByRole("listbox"), { key: "ArrowDown" });
    expect(document.activeElement).toBe(options[1]);

    fireEvent.keyDown(screen.getByRole("listbox"), { key: "ArrowUp" });
    expect(document.activeElement).toBe(options[0]);

    fireEvent.keyDown(screen.getByRole("listbox"), { key: "End" });
    expect(document.activeElement).toBe(options[2]);

    fireEvent.click(options[2]!);
    expect(onPick).toHaveBeenCalledWith(3);
  });

  it("closes on Escape and returns focus to the button that opened it", () => {
    render(
      <LibraryPicker
        libraries={[library(1, "TV"), library(2, "Movies")]}
        chosenId={1}
        onPick={vi.fn()}
      />,
    );

    const trigger = screen.getByRole("button", { name: /TV/ });
    fireEvent.click(trigger);
    expect(screen.getByRole("listbox")).toBeInTheDocument();

    fireEvent.keyDown(document, { key: "Escape" });

    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
    expect(document.activeElement).toBe(trigger);
  });

  it("finds a library by name once there are enough to search", () => {
    const many = Array.from({ length: 8 }, (_, i) =>
      library(i + 1, `Lib ${i + 1}`),
    );
    render(<LibraryPicker libraries={many} chosenId={1} onPick={vi.fn()} />);

    fireEvent.click(screen.getByRole("button", { name: /Lib 1$/ }));
    fireEvent.change(screen.getByPlaceholderText("Find a library"), {
      target: { value: "Lib 3" },
    });

    expect(screen.getAllByRole("option")).toHaveLength(1);
    expect(screen.getByRole("option")).toHaveTextContent("Lib 3");
  });
});
