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

function Layer({ name, onClose }: { name: string; onClose: () => void }) {
  const ref = useModalFocus<HTMLDivElement>({ onClose });
  return <div ref={ref} tabIndex={-1} role="dialog" aria-label={name} />;
}

it("closes only the newest of two open layers on Escape", () => {
  const closePanel = vi.fn();
  const closeDialog = vi.fn();
  render(
    <>
      <Layer name="Panel" onClose={closePanel} />
      <Layer name="Dialog" onClose={closeDialog} />
    </>,
  );

  fireEvent.keyDown(document, { key: "Escape" });

  expect(closeDialog).toHaveBeenCalledTimes(1);
  expect(closePanel).not.toHaveBeenCalled();
});

function ModalLayer({ modal }: { modal: boolean }) {
  const ref = useModalFocus<HTMLDivElement>({ onClose: () => undefined });
  return (
    <>
      <div
        ref={ref}
        tabIndex={-1}
        role="dialog"
        aria-modal={modal ? "true" : undefined}
        aria-label="Layer"
      >
        <button type="button">First</button>
        <button type="button">Last</button>
      </div>
      <button type="button">Behind</button>
    </>
  );
}

it("wraps Tab from the last control to the first inside a modal layer", () => {
  render(<ModalLayer modal />);
  screen.getByRole("button", { name: "Last" }).focus();

  fireEvent.keyDown(document, { key: "Tab" });

  expect(screen.getByRole("button", { name: "First" })).toHaveFocus();
});

it("wraps Shift+Tab from the first control to the last inside a modal layer", () => {
  render(<ModalLayer modal />);
  screen.getByRole("button", { name: "First" }).focus();

  fireEvent.keyDown(document, { key: "Tab", shiftKey: true });

  expect(screen.getByRole("button", { name: "Last" })).toHaveFocus();
});

it("lets Tab leave a layer that is not modal", () => {
  render(<ModalLayer modal={false} />);
  const last = screen.getByRole("button", { name: "Last" });
  last.focus();

  const allowed = fireEvent.keyDown(document, { key: "Tab" });

  expect(allowed).toBe(true);
  expect(last).toHaveFocus();
});

it("keeps focus where it is when the owner re-renders with a new onClose", () => {
  function Form() {
    const [name, setName] = useState("");
    const ref = useModalFocus<HTMLDivElement>({
      onClose: () => setName(""),
    });
    return (
      <div ref={ref} tabIndex={-1} role="dialog" aria-modal="true">
        <input
          aria-label="Name"
          value={name}
          onChange={(event) => setName(event.target.value)}
        />
      </div>
    );
  }
  render(<Form />);
  const field = screen.getByRole("textbox", { name: "Name" });
  field.focus();

  fireEvent.change(field, { target: { value: "Films" } });

  expect(field).toHaveValue("Films");
  expect(field).toHaveFocus();
});

it("ignores Escape while busy", () => {
  const onClose = vi.fn();
  render(<Harness busy onClose={onClose} />);
  fireEvent.click(screen.getByRole("button", { name: "Open" }));
  fireEvent.keyDown(document, { key: "Escape" });
  expect(onClose).not.toHaveBeenCalled();
});
