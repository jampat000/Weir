import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { ColumnsMenu } from "../../components/shared/columns-menu";
import { SortableColumnHeader } from "../../components/shared/sortable-column-header";
import type { TableColumnsConfig, TableSort } from "./table-columns";
import { useTableColumns } from "./use-table-columns";

type Id = "name" | "size" | "when";

const config: TableColumnsConfig<Id> = {
  tableId: "demo",
  sortable: true,
  defaultSort: { id: "name", direction: "asc" },
  columns: [
    { id: "name", label: "Name" },
    { id: "size", label: "Size", firstDirection: "desc" },
    { id: "when", label: "When" },
  ],
};

const STORAGE_KEY = "weir-table:demo";

function Demo({
  table = config,
  onSortChange,
}: {
  table?: TableColumnsConfig<Id>;
  onSortChange?: (sort: TableSort<Id> | null) => void;
}) {
  const columns = useTableColumns(table, { onSortChange });
  return (
    <>
      <ColumnsMenu table={columns} />
      <table {...columns.tableProps}>
        <thead>
          <tr>
            {columns.order.map((id) => (
              <SortableColumnHeader key={id} heading={columns.heading(id)} />
            ))}
          </tr>
        </thead>
      </table>
    </>
  );
}

function headings() {
  return screen
    .getAllByRole("columnheader")
    .map((header) => header.textContent);
}

beforeEach(() => {
  localStorage.clear();
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("sorting", () => {
  it("shows the starting sort on its heading, and no other", () => {
    render(<Demo />);

    expect(screen.getByRole("columnheader", { name: "Name" })).toHaveAttribute(
      "aria-sort",
      "ascending",
    );
    expect(
      screen.getByRole("columnheader", { name: "Size" }),
    ).not.toHaveAttribute("aria-sort");
  });

  it("sorts by a heading the way the column says to begin, and reverses on the next click", () => {
    render(<Demo />);

    fireEvent.click(screen.getByRole("button", { name: "Size" }));
    expect(screen.getByRole("columnheader", { name: "Size" })).toHaveAttribute(
      "aria-sort",
      "descending",
    );

    fireEvent.click(screen.getByRole("button", { name: "Size" }));
    expect(screen.getByRole("columnheader", { name: "Size" })).toHaveAttribute(
      "aria-sort",
      "ascending",
    );
    expect(
      screen.getByRole("columnheader", { name: "Name" }),
    ).not.toHaveAttribute("aria-sort");
  });

  it("tells the table about a sort that was chosen, and about the start being put back", () => {
    const onSortChange = vi.fn();
    render(<Demo onSortChange={onSortChange} />);

    fireEvent.click(screen.getByRole("button", { name: "When" }));
    expect(onSortChange).toHaveBeenLastCalledWith({
      id: "when",
      direction: "asc",
    });

    fireEvent.click(screen.getByRole("button", { name: "Columns" }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Reset columns" }));
    expect(onSortChange).toHaveBeenLastCalledWith({
      id: "name",
      direction: "asc",
    });
  });

  it("offers no sort on a table whose row order is the point", () => {
    render(<Demo table={{ ...config, sortable: false, defaultSort: null }} />);

    expect(
      screen.queryByRole("button", { name: "Size" }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole("columnheader", { name: "Size" }),
    ).toBeInTheDocument();
  });
});

describe("moving columns from the keyboard", () => {
  it("moves a column with Alt and an arrow, says where it went and keeps focus on its heading", () => {
    render(<Demo />);
    const size = screen.getByRole("button", { name: "Size" });
    size.focus();

    fireEvent.keyDown(size, { key: "ArrowLeft", altKey: true });

    expect(headings()).toEqual(["Size", "Name", "When"]);
    expect(screen.getByRole("status")).toHaveTextContent(
      "Size moved to position 1 of 3.",
    );
    expect(screen.getByRole("button", { name: "Size" })).toHaveFocus();
  });

  it("leaves the order alone at either end, and for an arrow without Alt", () => {
    render(<Demo />);

    fireEvent.keyDown(screen.getByRole("button", { name: "Name" }), {
      key: "ArrowLeft",
      altKey: true,
    });
    fireEvent.keyDown(screen.getByRole("button", { name: "Size" }), {
      key: "ArrowRight",
    });

    expect(headings()).toEqual(["Name", "Size", "When"]);
  });

  it("points every heading at the words that say how to move a column", () => {
    render(<Demo />);

    const hint = document.getElementById(
      screen
        .getByRole("button", { name: "Size" })
        .getAttribute("aria-describedby")!,
    );
    expect(hint).toHaveTextContent(/Alt with the left or right arrow/);
    expect(hint).toHaveTextContent(/click again to reverse/);
  });
});

describe("remembering the layout in this browser", () => {
  it("saves a move and a sort, and the next visit finds them", () => {
    const first = render(<Demo />);
    fireEvent.keyDown(screen.getByRole("button", { name: "When" }), {
      key: "ArrowLeft",
      altKey: true,
    });
    fireEvent.click(screen.getByRole("button", { name: "Size" }));
    first.unmount();

    render(<Demo />);

    expect(headings()).toEqual(["Name", "When", "Size"]);
    expect(screen.getByRole("columnheader", { name: "Size" })).toHaveAttribute(
      "aria-sort",
      "descending",
    );
  });

  it("keeps each table's layout apart from the others'", () => {
    const first = render(<Demo />);
    fireEvent.click(screen.getByRole("button", { name: "Size" }));
    first.unmount();

    render(<Demo table={{ ...config, tableId: "other" }} />);

    expect(screen.getByRole("columnheader", { name: "Name" })).toHaveAttribute(
      "aria-sort",
      "ascending",
    );
  });

  it("puts the table back as it started with Reset columns, and forgets what it saved", () => {
    render(<Demo />);
    fireEvent.keyDown(screen.getByRole("button", { name: "When" }), {
      key: "ArrowLeft",
      altKey: true,
    });
    fireEvent.click(screen.getByRole("button", { name: "Size" }));
    expect(localStorage.getItem(STORAGE_KEY)).not.toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "Columns" }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Reset columns" }));

    expect(headings()).toEqual(["Name", "Size", "When"]);
    expect(screen.getByRole("columnheader", { name: "Name" })).toHaveAttribute(
      "aria-sort",
      "ascending",
    );
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it("works on a browser that will not remember anything", () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("blocked");
    });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("blocked");
    });

    render(<Demo />);
    fireEvent.click(screen.getByRole("button", { name: "Size" }));

    expect(screen.getByRole("columnheader", { name: "Size" })).toHaveAttribute(
      "aria-sort",
      "descending",
    );
  });

  it("starts from the table's own layout when what was saved cannot be read", () => {
    localStorage.setItem(STORAGE_KEY, "{not json");

    render(<Demo />);

    expect(headings()).toEqual(["Name", "Size", "When"]);
  });
});
