import { describe, expect, it } from "vitest";

import { NAV_GROUPS, pageMeta } from "./nav-model";

describe("pageMeta", () => {
  it.each([
    ["/", "", "Processing"],
    ["/history", "", "History"],
    ["/library", "", "Library"],
    ["/system", "?tab=logs", "System"],
    ["/settings", "", "Workflows"],
    ["/settings", "?tab=rules", "Rules"],
    ["/settings", "?tab=media-managers&library=2", "Media managers"],
    ["/settings", "?tab=notifications", "Alerts"],
  ])("titles %s%s as %s", (pathname, search, title) => {
    expect(pageMeta({ pathname, search })).toMatchObject({
      title,
      ownsHeading: true,
    });
  });

  it("gives every page an eyebrow that says what it is for", () => {
    for (const group of NAV_GROUPS) {
      for (const item of group.items) {
        expect(pageMeta(placeOf(item.to)).eyebrow).not.toBe("");
      }
    }
  });

  it("leaves the heading to a page the menu does not list", () => {
    expect(pageMeta({ pathname: "/dashboard", search: "" })).toMatchObject({
      ownsHeading: false,
    });
  });

  it("does not mistake a longer path for a page that starts the same way", () => {
    expect(pageMeta({ pathname: "/library-old", search: "" }).ownsHeading).toBe(
      false,
    );
  });
});

function placeOf(to: string) {
  const [pathname, search = ""] = to.split("?");
  return { pathname, search: search ? `?${search}` : "" };
}

describe("the menu", () => {
  it("has exactly one current item for every page it lists", () => {
    for (const group of NAV_GROUPS) {
      for (const item of group.items) {
        const place = placeOf(item.to);
        const current = NAV_GROUPS.flatMap((g) => g.items).filter((candidate) =>
          candidate.isCurrent(place),
        );
        expect(current.map((candidate) => candidate.id)).toEqual([item.id]);
      }
    }
  });
});
