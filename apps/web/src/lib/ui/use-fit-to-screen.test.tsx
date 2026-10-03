import { render } from "@testing-library/react";
import { useRef } from "react";
import { afterEach, describe, expect, it } from "vitest";

import { useFitToScreen } from "./use-fit-to-screen";

const MIN_HEIGHT = 600;

function Page({ enabled }: { enabled: boolean }) {
  const ref = useRef<HTMLDivElement>(null);
  useFitToScreen(ref, enabled, MIN_HEIGHT);
  return (
    <main style={{ paddingBottom: "28px" }}>
      <div ref={ref} data-testid="page" />
    </main>
  );
}

function windowHeight(height: number) {
  Object.defineProperty(window, "innerHeight", {
    configurable: true,
    value: height,
  });
}

const page = (container: HTMLElement) =>
  container.querySelector<HTMLElement>("[data-testid=page]")!;

function pageTop(top: number, container: HTMLElement) {
  page(container).getBoundingClientRect = () => ({ top }) as DOMRect;
}

afterEach(() => windowHeight(768));

describe("a page as tall as what is left of the window", () => {
  it("takes the window's height less what is above it and the main area's padding under it", () => {
    windowHeight(900);
    const { container, rerender } = render(<Page enabled={false} />);
    pageTop(92, container);

    rerender(<Page enabled />);

    expect(page(container).style.height).toBe(`${900 - 92 - 28}px`);
  });

  it("follows the window when it is resized", () => {
    windowHeight(900);
    const { container, rerender } = render(<Page enabled={false} />);
    pageTop(92, container);
    rerender(<Page enabled />);

    windowHeight(1080);
    window.dispatchEvent(new Event("resize"));

    expect(page(container).style.height).toBe(`${1080 - 92 - 28}px`);
  });

  it("is never shorter than its least, so a short window scrolls rather than crushing the page", () => {
    windowHeight(500);
    const { container, rerender } = render(<Page enabled={false} />);
    pageTop(92, container);

    rerender(<Page enabled />);

    expect(page(container).style.height).toBe(`${MIN_HEIGHT}px`);
  });

  it("leaves the page to its content when it is switched off, and gives its height back", () => {
    windowHeight(900);
    const { container, rerender } = render(<Page enabled={false} />);
    pageTop(92, container);
    rerender(<Page enabled />);
    expect(page(container).style.height).not.toBe("");

    rerender(<Page enabled={false} />);

    expect(page(container).style.height).toBe("");
  });
});
