import { fireEvent, render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";

import { MmListboxPicker, type MmListboxOption } from "./mm-listbox-picker";

const OPTIONS: MmListboxOption[] = [
  { value: "a", label: "Alpha" },
  { value: "b", label: "Bravo" },
  { value: "c", label: "Charlie" },
];

function renderPicker(
  overrides: Partial<Parameters<typeof MmListboxPicker>[0]> = {},
) {
  const onChange = vi.fn();
  render(
    <MmListboxPicker
      options={OPTIONS}
      value="a"
      onChange={onChange}
      {...overrides}
    />,
  );
  return { onChange };
}

it("does not truncate a long selected label", () => {
  const longLabel =
    "A very long option label that would previously be cut off by the trigger";
  renderPicker({
    options: [{ value: "long", label: longLabel }],
    value: "long",
  });

  const trigger = screen.getByRole("button");
  expect(trigger.querySelector("span")?.className).not.toMatch(/\btruncate\b/);
  expect(screen.getByText(longLabel)).toBeInTheDocument();
});

it("selects an option on click and closes the panel", () => {
  const { onChange } = renderPicker();
  fireEvent.click(screen.getByRole("button", { name: "Alpha" }));
  fireEvent.click(screen.getByRole("option", { name: "Charlie" }));

  expect(onChange).toHaveBeenCalledWith("c");
  expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
});

it("closes when a click lands outside the picker", () => {
  render(
    <div>
      <MmListboxPicker options={OPTIONS} value="a" onChange={vi.fn()} />
      <button type="button">Outside</button>
    </div>,
  );
  fireEvent.click(screen.getByRole("button", { name: "Alpha" }));
  fireEvent.mouseDown(screen.getByRole("button", { name: "Outside" }));

  expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
});

it("closes on Escape", () => {
  renderPicker();
  fireEvent.click(screen.getByRole("button", { name: "Alpha" }));
  fireEvent.keyDown(document, { key: "Escape" });

  expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
});

it("never opens while disabled", () => {
  renderPicker({ disabled: true });
  fireEvent.click(screen.getByRole("button", { name: "Alpha" }));

  expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
});

it("moves the active option with ArrowDown and ArrowUp", () => {
  renderPicker();
  const trigger = screen.getByRole("button", { name: "Alpha" });
  fireEvent.click(trigger);
  const options = screen.getAllByRole("option");

  fireEvent.keyDown(trigger, { key: "ArrowDown" });
  expect(options[1]).toHaveFocus();

  fireEvent.keyDown(options[1], { key: "ArrowDown" });
  expect(options[2]).toHaveFocus();

  fireEvent.keyDown(options[2], { key: "ArrowUp" });
  expect(options[1]).toHaveFocus();
});

it("jumps to the first and last option with Home and End", () => {
  renderPicker();
  const trigger = screen.getByRole("button", { name: "Alpha" });
  fireEvent.click(trigger);
  const options = screen.getAllByRole("option");

  fireEvent.keyDown(trigger, { key: "End" });
  expect(options[2]).toHaveFocus();

  fireEvent.keyDown(options[2], { key: "Home" });
  expect(options[0]).toHaveFocus();
});

it("jumps to the option matching a typed letter", () => {
  renderPicker();
  const trigger = screen.getByRole("button", { name: "Alpha" });
  fireEvent.click(trigger);
  const options = screen.getAllByRole("option");

  fireEvent.keyDown(trigger, { key: "b" });
  expect(options[1]).toHaveFocus();
});

it("activates the focused option with Enter", () => {
  const { onChange } = renderPicker();
  const trigger = screen.getByRole("button", { name: "Alpha" });
  fireEvent.click(trigger);
  const options = screen.getAllByRole("option");

  fireEvent.keyDown(trigger, { key: "ArrowDown" });
  fireEvent.keyDown(options[1], { key: "Enter" });

  expect(onChange).toHaveBeenCalledWith("b");
  expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
});

it("activates the focused option with Space", () => {
  const { onChange } = renderPicker();
  const trigger = screen.getByRole("button", { name: "Alpha" });
  fireEvent.click(trigger);
  const options = screen.getAllByRole("option");

  fireEvent.keyDown(trigger, { key: "ArrowDown" });
  fireEvent.keyDown(options[1], { key: " " });

  expect(onChange).toHaveBeenCalledWith("b");
  expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
});
