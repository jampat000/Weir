import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { MmOnOffSwitch } from "./mm-on-off-switch";

function switchOf(size?: "page" | "row") {
  render(
    <MmOnOffSwitch
      id="s"
      label="Movies enabled"
      enabled
      disabled={false}
      onChange={() => undefined}
      layout="control"
      size={size}
    />,
  );
  return screen.getByRole("radiogroup", { name: "Movies enabled" });
}

describe("the On / Off switch", () => {
  it("is a page field's height unless it is told it stands on a table row", () => {
    expect(switchOf()).toHaveClass("h-(--mm-control-height-page)");
  });

  it("is a table row's height, with the row's buttons, when it stands on one", () => {
    const group = switchOf("row");

    expect(group).toHaveClass("h-(--mm-control-height-row)");
    expect(screen.getByRole("radio", { name: "On" })).toHaveClass("text-xs");
  });
});
