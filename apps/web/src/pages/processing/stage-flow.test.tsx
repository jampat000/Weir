import flowStyles from "../../styles/weir-processing-flow.css?raw";
import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { StageFlow } from "./stage-flow";

function steps(): HTMLElement[] {
  return within(screen.getByRole("list", { name: "Stages" })).getAllByRole(
    "listitem",
  );
}

function writeFill(): Element | null {
  return screen
    .getByRole("progressbar", { name: "Write progress" })
    .querySelector(".mm-flow__fill");
}

describe("StageFlow", () => {
  it("lists the stages in order as an ordered list", () => {
    render(<StageFlow position="checking" />);

    expect(screen.getByRole("list", { name: "Stages" }).tagName).toBe("OL");
    expect(steps().map((step) => step.textContent?.split(" (")[0])).toEqual([
      "Checking",
      "Plan",
      "Write",
      "Verify",
      "Hand back",
    ]);
  });

  it("names the current step to assistive technology and no other", () => {
    render(<StageFlow position="write" percent={30} />);

    const current = steps().filter(
      (step) => step.getAttribute("aria-current") === "step",
    );
    expect(current).toHaveLength(1);
    expect(current[0]).toHaveTextContent("Write (in progress)");
  });

  it("says in words which steps are done and which are still to come", () => {
    render(<StageFlow position="write" percent={30} />);

    expect(steps()[0]).toHaveTextContent("Checking (done)");
    expect(steps()[1]).toHaveTextContent("Plan (done)");
    expect(steps()[3]).toHaveTextContent("Verify (up next)");
    expect(steps()[4]).toHaveTextContent("Hand back (up next)");
  });

  it("draws a tick on a finished step and a dot on the current one", () => {
    render(<StageFlow position="plan" />);

    expect(steps()[0].querySelector(".mm-flow__glyph")).not.toBeNull();
    expect(steps()[1].querySelector(".mm-flow__dot")).not.toBeNull();
    expect(steps()[2].querySelector(".mm-flow__glyph")).toBeNull();
    expect(steps()[2].querySelector(".mm-flow__dot")).toBeNull();
  });

  it("gives the first step no line into it and every other step one", () => {
    render(<StageFlow position="checking" />);

    expect(steps()[0].querySelector(".mm-flow__link")).toBeNull();
    for (const step of steps().slice(1)) {
      expect(step.querySelector(".mm-flow__link")).not.toBeNull();
    }
  });

  it("fills the line into a finished step, sweeps the one into the current step, and leaves the rest empty", () => {
    render(<StageFlow position="verify" />);

    const links = steps()
      .slice(1)
      .map((step) => step.querySelector(".mm-flow__link")?.className);
    expect(links).toEqual([
      "mm-flow__link mm-flow__link--full",
      "mm-flow__link mm-flow__link--full",
      "mm-flow__link mm-flow__link--busy",
      "mm-flow__link mm-flow__link--empty",
    ]);
  });

  it("sets the line into Write to the pass's live percent", () => {
    render(<StageFlow position="write" percent={63.7} />);

    expect(
      screen.getByRole("progressbar", { name: "Write progress" }),
    ).toHaveAttribute("aria-valuenow", "64");
    expect(writeFill()?.getAttribute("style")).toContain(
      "--mm-live-fill: 63.7",
    );
  });

  it("moves the line when the live percent changes, for the same file", () => {
    const { rerender } = render(<StageFlow position="write" percent={17} />);

    rerender(<StageFlow position="write" percent={63} />);

    expect(writeFill()?.getAttribute("style")).toContain("--mm-live-fill: 63");
  });

  it("keeps a just-started write visible with a thin sliver", () => {
    render(<StageFlow position="write" percent={0} />);

    expect(writeFill()?.getAttribute("style")).toContain("--mm-live-fill: 2");
  });

  it("never sets its own transition or animation inline, leaving motion entirely to CSS", () => {
    render(<StageFlow position="write" percent={55} />);

    for (const element of document.querySelectorAll("[style]")) {
      expect(element.getAttribute("style")).not.toMatch(/transition|animation/);
    }
  });

  it("offers no progress bar before Write starts", () => {
    render(<StageFlow position="checking" />);

    expect(screen.queryByRole("progressbar")).toBeNull();
  });

  it("ends with every step ticked and none current once the file is done", () => {
    render(<StageFlow position="done" />);

    for (const step of steps()) {
      expect(step).toHaveTextContent("(done)");
      expect(step).not.toHaveAttribute("aria-current");
    }
    expect(document.querySelectorAll(".mm-flow__link--full")).toHaveLength(4);
  });

  it("marks the step a file failed at, in words, with the reason beside it", () => {
    render(
      <StageFlow
        position="write"
        failure={{ at: "verify", reason: "The new file would not play." }}
      />,
    );

    expect(steps()[3]).toHaveTextContent("Verify (failed)");
    expect(steps()[3]).toHaveClass("mm-flow__step--failed");
    expect(steps()[2]).toHaveTextContent("Write (done)");
    expect(steps()[4]).toHaveTextContent("Hand back (up next)");
    expect(
      screen.getByText("The new file would not play."),
    ).toBeInTheDocument();
  });

  it("shows a rejection on the check or plan step", () => {
    render(
      <StageFlow
        position="checking"
        failure={{ at: "plan", reason: "Nothing here needs changing." }}
      />,
    );

    expect(steps()[1]).toHaveTextContent("Plan (failed)");
    expect(steps()[0]).toHaveTextContent("Checking (done)");
    expect(screen.queryByRole("progressbar")).toBeNull();
  });

  it("names no current step once a file has failed", () => {
    render(
      <StageFlow
        position="write"
        failure={{ at: "write", reason: "Out of space." }}
      />,
    );

    expect(
      steps().filter((step) => step.hasAttribute("aria-current")),
    ).toHaveLength(0);
  });
});

/**
 * jsdom does not apply the stylesheet, so this reads the reduced-motion block itself: every looping
 * animation and every transition the flow uses has to be switched off inside it.
 */
describe("StageFlow's reduced-motion styles", () => {
  const reduced =
    /@media \(prefers-reduced-motion: reduce\) \{([\s\S]*)\n\}/.exec(
      flowStyles,
    )?.[1] ?? "";

  it("has a reduced-motion block", () => {
    expect(reduced).not.toBe("");
  });

  it("stops the pulse, the tick's drawing, the shimmer and the sweep", () => {
    expect(reduced).toMatch(
      /\.mm-flow__step--now \.mm-flow__node::after,[^{]*\.mm-flow__step--done \.mm-flow__glyph path,[^{]*\.mm-flow__link--live \.mm-flow__fill::after,[^{]*\.mm-flow__link--busy \.mm-flow__fill::before \{\s*animation: none;/,
    );
  });

  it("stops the line's fill and the nodes' colour from transitioning", () => {
    expect(reduced).toMatch(
      /\.mm-flow__node,\s*\.mm-flow__label,\s*\.mm-flow__fill \{\s*transition: none;/,
    );
  });
});
