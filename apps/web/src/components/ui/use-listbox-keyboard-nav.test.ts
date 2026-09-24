import { act, renderHook } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { useListboxKeyboardNav } from "./use-listbox-keyboard-nav";

type Option = { label: string };

function options(...labels: string[]): Option[] {
  return labels.map((label) => ({ label }));
}

function setup(initialProps: {
  isOpen: boolean;
  initialIndex: number;
  opts: Option[];
}) {
  const onActivate = vi.fn();
  const hook = renderHook(
    (props: typeof initialProps) =>
      useListboxKeyboardNav(props.opts, {
        isOpen: props.isOpen,
        initialIndex: props.initialIndex,
        onActivate,
      }),
    { initialProps },
  );
  return { onActivate, ...hook };
}

describe("useListboxKeyboardNav", () => {
  it("starts roving focus at the initial index the moment the panel opens", () => {
    const { result, rerender } = setup({
      isOpen: false,
      initialIndex: 1,
      opts: options("Alpha", "Bravo", "Charlie"),
    });

    rerender({
      isOpen: true,
      initialIndex: 1,
      opts: options("Alpha", "Bravo", "Charlie"),
    });

    expect(result.current.activeIndex).toBe(1);
  });

  it("clamps an initial index past the end of the option list", () => {
    const { result, rerender } = setup({
      isOpen: false,
      initialIndex: 9,
      opts: options("Alpha", "Bravo"),
    });

    rerender({
      isOpen: true,
      initialIndex: 9,
      opts: options("Alpha", "Bravo"),
    });

    expect(result.current.activeIndex).toBe(1);
  });

  it("does not reset the active option while already open, even when options or the initial index change", () => {
    const { result, rerender } = setup({
      isOpen: true,
      initialIndex: 0,
      opts: options("Alpha", "Bravo", "Charlie"),
    });

    act(() => result.current.setActive(2));
    expect(result.current.activeIndex).toBe(2);

    // A filtered or lengthened option list while the panel stays open must not fight the person's
    // own navigation by jumping back to the initial index.
    rerender({
      isOpen: true,
      initialIndex: 0,
      opts: options("Alpha", "Bravo", "Charlie", "Delta"),
    });
    expect(result.current.activeIndex).toBe(2);

    rerender({
      isOpen: true,
      initialIndex: 2,
      opts: options("Alpha", "Bravo", "Charlie", "Delta"),
    });
    expect(result.current.activeIndex).toBe(2);
  });

  it("starts at the initial index again the next time it reopens", () => {
    const { result, rerender } = setup({
      isOpen: true,
      initialIndex: 0,
      opts: options("Alpha", "Bravo", "Charlie"),
    });

    act(() => result.current.setActive(2));
    rerender({
      isOpen: false,
      initialIndex: 0,
      opts: options("Alpha", "Bravo", "Charlie"),
    });
    rerender({
      isOpen: true,
      initialIndex: 1,
      opts: options("Alpha", "Bravo", "Charlie"),
    });

    expect(result.current.activeIndex).toBe(1);
  });
});
