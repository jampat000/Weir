import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";

import * as api from "../../../../lib/processing/libraries-api";
import { LibrariesTab } from "./libraries-tab";
import {
  asOperator,
  asViewer,
  library,
  wrapper,
} from "./library-test-fixtures";

afterEach(() => {
  vi.restoreAllMocks();
});

function threeWorkflows() {
  return [
    library({ id: 1, name: "Movies", display_order: 0 }),
    library({ id: 2, name: "TV", media_type: "tv", display_order: 1 }),
    library({ id: 3, name: "Kids", display_order: 2 }),
  ];
}

function namesInOrder() {
  return screen
    .getAllByRole("rowheader")
    .map(
      (header) => header.querySelector(".mm-workflow-name__text")?.textContent,
    );
}

it("shows the watched folder and the folder cleaned into in columns of their own", async () => {
  asOperator();
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue([library()]);

  render(<LibrariesTab />, { wrapper });

  const row = within(await screen.findByTestId("processing-library-1"));
  expect(
    screen.getByRole("columnheader", { name: "Watches" }),
  ).toBeInTheDocument();
  expect(
    screen.getByRole("columnheader", { name: "Cleans into" }),
  ).toBeInTheDocument();
  expect(row.getByTitle("/srv/movies/in")).toHaveTextContent("/srv/movies/in");
  expect(row.getByTitle("/srv/movies/out")).toHaveTextContent(
    "/srv/movies/out",
  );
});

it("says what applies when a workflow has no folders to show", async () => {
  asOperator();
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue([
    library({
      id: 1,
      name: "Library only",
      watched_folder: "",
      output_folder: "",
    }),
    library({
      id: 2,
      name: "Half set up",
      display_order: 1,
      output_folder: "",
    }),
  ]);

  render(<LibrariesTab />, { wrapper });

  const libraryOnly = within(await screen.findByTestId("processing-library-1"));
  expect(libraryOnly.getByText("Not watching a folder")).toBeInTheDocument();
  expect(libraryOnly.getByText("Cleans in place")).toBeInTheDocument();
  const halfSetUp = within(screen.getByTestId("processing-library-2"));
  expect(halfSetUp.getByText("/srv/movies/in")).toBeInTheDocument();
  expect(halfSetUp.getByText("Needs an output folder")).toBeInTheDocument();
});

it("numbers the workflows by priority and says what the order decides", async () => {
  asOperator();
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue(threeWorkflows());

  render(<LibrariesTab />, { wrapper });

  const rows = await screen.findAllByTestId(/^processing-library-\d$/);
  expect(
    rows.map((row) => row.querySelector(".mm-priority__number")?.textContent),
  ).toEqual(["1", "2", "3"]);
  expect(screen.getByTestId("workflow-priority-note")).toHaveTextContent(
    "When a file could go to more than one workflow, the higher one takes it.",
  );
});

it("moves a workflow with Alt and an arrow, saves the new order at once and says where it went", async () => {
  asOperator();
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue(threeWorkflows());
  const reorder = vi
    .spyOn(api, "reorderProcessingLibraries")
    .mockResolvedValue(threeWorkflows());

  render(<LibrariesTab />, { wrapper });
  fireEvent.keyDown(await screen.findByRole("button", { name: "Move TV" }), {
    key: "ArrowUp",
    altKey: true,
  });

  await waitFor(() => expect(reorder).toHaveBeenCalledWith([2, 1, 3]));
  expect(
    screen.getByTestId("workflow-priority-announcement"),
  ).toHaveTextContent("TV moved to position 1 of 3.");
});

it("holds a picked-up workflow until it is dropped, and puts it back on Escape", async () => {
  asOperator();
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue(threeWorkflows());
  const reorder = vi
    .spyOn(api, "reorderProcessingLibraries")
    .mockResolvedValue(threeWorkflows());

  render(<LibrariesTab />, { wrapper });
  const grip = await screen.findByRole("button", { name: "Move Movies" });
  fireEvent.keyDown(grip, { key: " " });
  fireEvent.keyDown(grip, { key: "ArrowDown" });

  expect(namesInOrder()).toEqual(["TV", "Movies", "Kids"]);
  expect(reorder).not.toHaveBeenCalled();

  fireEvent.keyDown(grip, { key: "Escape" });

  expect(namesInOrder()).toEqual(["Movies", "TV", "Kids"]);
  expect(reorder).not.toHaveBeenCalled();

  fireEvent.keyDown(grip, { key: " " });
  fireEvent.keyDown(grip, { key: "ArrowDown" });
  fireEvent.keyDown(grip, { key: " " });

  await waitFor(() => expect(reorder).toHaveBeenCalledWith([2, 1, 3]));
});

it("says why a new order could not be saved and shows the saved one again", async () => {
  asOperator();
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue(threeWorkflows());
  vi.spyOn(api, "reorderProcessingLibraries").mockRejectedValue(
    new Error("The server said no."),
  );

  render(<LibrariesTab />, { wrapper });
  fireEvent.keyDown(await screen.findByRole("button", { name: "Move Kids" }), {
    key: "ArrowUp",
    altKey: true,
  });

  expect(
    await screen.findByTestId("processing-library-notice"),
  ).toHaveTextContent("The server said no.");
  await waitFor(() => expect(namesInOrder()).toEqual(["Movies", "TV", "Kids"]));
});

it("leaves the order alone for a viewer", async () => {
  asViewer();
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue(threeWorkflows());

  render(<LibrariesTab />, { wrapper });

  expect(await screen.findByText("Kids")).toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: /^Move / }),
  ).not.toBeInTheDocument();
});

it("lets the columns be moved but never sorts the rows, whose order is the priority", async () => {
  asOperator();
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue(threeWorkflows());

  render(<LibrariesTab />, { wrapper });
  await screen.findAllByTestId(/^processing-library-\d$/);

  const headings = () =>
    screen.getAllByRole("columnheader").map((header) => header.textContent);
  expect(headings().slice(0, 3)).toEqual(["Priority", "Workflow", "Kind"]);
  expect(
    screen.getByRole("columnheader", { name: "Kind" }).querySelector("button"),
  ).toBeNull();

  fireEvent.keyDown(screen.getByRole("columnheader", { name: "Kind" }), {
    key: "ArrowLeft",
    altKey: true,
  });

  expect(headings().slice(0, 3)).toEqual(["Priority", "Kind", "Workflow"]);
  expect(namesInOrder()).toEqual(["Movies", "TV", "Kids"]);
});
