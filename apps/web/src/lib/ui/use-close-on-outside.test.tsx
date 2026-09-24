import { fireEvent, render, screen } from "@testing-library/react";
import { useRef, useState } from "react";
import { expect, it, vi } from "vitest";

import { useCloseOnOutsideAndEscape } from "./use-close-on-outside";

function Menu() {
  const [open, setOpen] = useState(true);
  const ref = useRef<HTMLDivElement>(null);
  useCloseOnOutsideAndEscape(open, () => setOpen(false), ref);
  return (
    <div>
      <div ref={ref} data-testid="menu">
        {open ? "Open" : "Closed"}
      </div>
      <button type="button">Outside</button>
    </div>
  );
}

it("closes on Escape", () => {
  render(<Menu />);

  fireEvent.keyDown(document, { key: "Escape" });

  expect(screen.getByTestId("menu")).toHaveTextContent("Closed");
});

it("closes on a pointer down outside the container", () => {
  render(<Menu />);

  fireEvent.mouseDown(screen.getByRole("button", { name: "Outside" }));

  expect(screen.getByTestId("menu")).toHaveTextContent("Closed");
});

it("stays open for a pointer down inside the container", () => {
  render(<Menu />);

  fireEvent.mouseDown(screen.getByTestId("menu"));

  expect(screen.getByTestId("menu")).toHaveTextContent("Open");
});

it("does nothing while closed", () => {
  const onClose = vi.fn();
  const ref = { current: document.createElement("div") };

  function Closed() {
    useCloseOnOutsideAndEscape(false, onClose, ref);
    return null;
  }
  render(<Closed />);

  fireEvent.keyDown(document, { key: "Escape" });

  expect(onClose).not.toHaveBeenCalled();
});
