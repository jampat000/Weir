import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { LibraryPager, pageRange } from "./library-pager";

describe("pageRange", () => {
  it("says nothing when every file fits on the first page", () => {
    expect(pageRange(1, 200, 14, 14)).toBeNull();
  });

  it("shows the slice a page holds once there is more than one page", () => {
    expect(pageRange(2, 200, 200, 450)).toBe("Showing 201–400 of 450");
  });

  it("says when a page that once had files now has none", () => {
    // The library shrank under a page reached before it did, e.g. a rescan or another clean.
    expect(pageRange(3, 200, 0, 450)).toBe("Showing none of 450");
  });
});

describe("LibraryPager", () => {
  it("renders nothing when the whole library fits on one page", () => {
    const { container } = render(
      <LibraryPager
        page={1}
        pageSize={200}
        shown={14}
        total={14}
        loading={false}
        hasSelection={false}
        onPage={vi.fn()}
      />,
    );
    expect(container).toBeEmptyDOMElement();
  });

  it("a library over 200 files shows the count and reaches every page", () => {
    const onPage = vi.fn();
    render(
      <LibraryPager
        page={1}
        pageSize={200}
        shown={200}
        total={450}
        loading={false}
        hasSelection={false}
        onPage={onPage}
      />,
    );

    expect(screen.getByTestId("library-range")).toHaveTextContent(
      "Showing 1–200 of 450",
    );
    expect(screen.getByRole("button", { name: "Previous" })).toBeDisabled();
    const next = screen.getByRole("button", { name: "Next" });
    expect(next).toBeEnabled();

    fireEvent.click(next);
    expect(onPage).toHaveBeenCalledWith(2);
  });

  it("disables Next on the last page", () => {
    render(
      <LibraryPager
        page={3}
        pageSize={200}
        shown={50}
        total={450}
        loading={false}
        hasSelection={false}
        onPage={vi.fn()}
      />,
    );

    expect(screen.getByRole("button", { name: "Previous" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Next" })).toBeDisabled();
  });

  it("says a selection is for this page only, once something is selected", () => {
    render(
      <LibraryPager
        page={1}
        pageSize={200}
        shown={200}
        total={450}
        loading={false}
        hasSelection
        onPage={vi.fn()}
      />,
    );

    expect(
      screen.getByText(/Your selection is for this page/),
    ).toBeInTheDocument();
  });
});
