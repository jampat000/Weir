/**
 * The action button's contract: a disabled button looks disabled on its own, from the DOM attribute alone.
 *
 * This is here because the opposite was true for a long time. The helper used to take a `disabled` option that
 * changed the look, and 81 of 145 call sites never passed it, so those buttons refused the click while still
 * showing a gold border, a shadow and a pointer cursor. The option is gone; these tests pin what replaced it.
 */
import { describe, expect, it } from "vitest";
import { mmActionButtonClass } from "./mm-control-roles";

const VARIANTS = ["primary", "secondary", "tertiary"] as const;

/** The utilities whose roots are two words, longest first so `translate-y` is not read as `translate`. */
const ROOTS = [
  "translate-y",
  "border",
  "bg",
  "shadow",
  "text",
  "opacity",
  "cursor",
  "brightness",
  "ring",
];

/** Which property a utility sets, ignoring its value and any leading minus. */
function root(utility: string): string {
  const bare = utility.replace(/^-/, "");
  return (
    ROOTS.find((name) => bare === name || bare.startsWith(`${name}-`)) ?? bare
  );
}

function classesFor(variant: (typeof VARIANTS)[number]): string[] {
  return mmActionButtonClass({ variant }).split(/\s+/).filter(Boolean);
}

function rootsWithPrefix(classes: string[], prefix: string): Set<string> {
  return new Set(
    classes
      .filter((name) => name.startsWith(prefix))
      .map((name) => root(name.slice(prefix.length))),
  );
}

describe("mmActionButtonClass", () => {
  it.each(VARIANTS)(
    "gives a %s button a disabled look of its own",
    (variant) => {
      const classes = classesFor(variant);

      expect(classes).toContain("disabled:cursor-not-allowed");
      // Border, background and text all change, so a disabled button is never just a faded enabled one.
      for (const property of ["border", "bg", "text", "opacity"]) {
        expect(
          rootsWithPrefix(classes, "disabled:"),
          `${variant} must set ${property} when disabled`,
        ).toContain(property);
      }
    },
  );

  it.each(VARIANTS)(
    "stops every %s hover effect from firing on a disabled button",
    (variant) => {
      // CSS :hover matches a disabled button, so each hover property needs a disabled:hover counterpart.
      // Adding a new hover utility without one fails here rather than in someone's face.
      const classes = classesFor(variant);
      const hovered = rootsWithPrefix(classes, "hover:");
      const answered = rootsWithPrefix(classes, "disabled:hover:");

      expect(hovered.size).toBeGreaterThan(0);
      for (const property of hovered) {
        expect(
          answered,
          `${variant} changes ${property} on hover, so disabled:hover: must put it back`,
        ).toContain(property);
      }
    },
  );

  it("still gives an enabled button its ordinary look", () => {
    // The disabled rules are additions; nothing about a working button changed when they arrived.
    const primary = classesFor("primary");
    expect(primary).toContain("cursor-pointer");
    expect(primary).toContain("border-[var(--mm-gold)]");
    expect(classesFor("secondary")).toContain("border-[var(--mm-border)]");
    expect(classesFor("tertiary")).toContain("bg-transparent");
  });
});
