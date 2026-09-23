import { fireEvent, render, screen } from "@testing-library/react";
import { useState } from "react";
import { expect, it, vi } from "vitest";

import { useModalFocus } from "./use-modal-focus";

function Harness({
  busy = false,
  onClose,
}: {
  busy?: boolean;
  onClose: () => void;
}) {
  const [open, setOpen] = useState(false);
  const ref = useModalFocus<HTMLDivElement>({
    open,
    busy,
    onClose: () => {
      onClose();
      setOpen(false);
    },
  });
  return (
    <>
      <button type="button" onClick={() => setOpen(true)}>
        Open
      </button>
      {open ? (
        <div ref={ref} tabIndex={-1} role="dialog" aria-label="Panel" />
      ) : null}
    </>
  );
}

it("moves focus in, closes on Escape and hands focus back", () => {
  const onClose = vi.fn();
  render(<Harness onClose={onClose} />);
  const opener = screen.getByRole("button", { name: "Open" });
  opener.focus();
  fireEvent.click(opener);

  expect(screen.getByRole("dialog")).toHaveFocus();
  fireEvent.keyDown(document, { key: "Escape" });
  expect(onClose).toHaveBeenCalledTimes(1);
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  expect(opener).toHaveFocus();
});

it("ignores Escape while busy", () => {
  const onClose = vi.fn();
  render(<Harness busy onClose={onClose} />);
  fireEvent.click(screen.getByRole("button", { name: "Open" }));
  fireEvent.keyDown(document, { key: "Escape" });
  expect(onClose).not.toHaveBeenCalled();
});
