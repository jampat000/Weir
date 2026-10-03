import { afterEach, describe, expect, it, vi } from "vitest";

import { NEEDS_PANEL_ID, showNeedsPanel } from "./show-needs-panel";

afterEach(() => {
  document.body.replaceChildren();
});

describe("taking the person to the Needs you panel", () => {
  it("scrolls the panel into view and moves focus to it", () => {
    const panel = document.createElement("section");
    panel.id = NEEDS_PANEL_ID;
    panel.tabIndex = -1;
    const scrollIntoView = vi.fn();
    panel.scrollIntoView = scrollIntoView;
    document.body.append(panel);

    showNeedsPanel();

    expect(scrollIntoView).toHaveBeenCalledWith({ block: "start" });
    expect(panel).toHaveFocus();
  });

  it("does nothing on a page that has no such panel", () => {
    expect(() => showNeedsPanel()).not.toThrow();
  });
});
