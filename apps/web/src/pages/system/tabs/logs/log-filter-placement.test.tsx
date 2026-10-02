import { screen } from "@testing-library/react";
import {
  afterEach,
  beforeAll,
  beforeEach,
  describe,
  expect,
  it,
  vi,
} from "vitest";

import { logPage, renderLog, stubEventSource } from "./log-test-support";

const mocks = vi.hoisted(() => ({ fetchSystemLog: vi.fn() }));

vi.mock("../../../../lib/system/system-log-api", async (importOriginal) => ({
  ...(await importOriginal<
    typeof import("../../../../lib/system/system-log-api")
  >()),
  fetchSystemLog: mocks.fetchSystemLog,
}));

vi.mock("../../../../lib/settings/queries", async (importOriginal) => ({
  ...(await importOriginal<
    typeof import("../../../../lib/settings/queries")
  >()),
  useAppSettingsQuery: () => ({ data: { app_timezone: "UTC" } }),
}));

vi.mock("../../../../lib/processing/libraries-queries", () => ({
  useProcessingLibrariesQuery: () => ({ data: [{ id: 1, name: "Movies" }] }),
}));

vi.mock("../../../../lib/auth/queries", () => ({
  useMeQuery: () => ({ data: { id: 1, username: "alice", role: "operator" } }),
}));

vi.mock("../../../../lib/pause/pause-queries", () => ({
  usePauseQuery: () => ({ data: { paused: false } }),
}));

beforeAll(stubEventSource);

beforeEach(() => {
  mocks.fetchSystemLog.mockResolvedValue(logPage());
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.clearAllMocks();
});

/** jsdom has no layout: says the chips' box is too wide for as long as `unfit` says so. */
function chipsOverflowWhile(unfit: () => boolean) {
  vi.spyOn(Element.prototype, "scrollWidth", "get").mockImplementation(
    function (this: Element) {
      return this.classList.contains("mm-history-chips") && unfit() ? 500 : 0;
    },
  );
}

/** Says the Log card's header has run onto a second line for as long as `wrapped` says so. */
function cardHeaderWrapsWhile(wrapped: () => boolean) {
  const box = (top: number): DOMRect =>
    ({
      top,
      bottom: top + 20,
      left: 0,
      right: 0,
      width: 0,
      height: 20,
    }) as DOMRect;
  vi.spyOn(Element.prototype, "getBoundingClientRect").mockImplementation(
    function (this: Element) {
      const inCardHeader = this.parentElement?.matches("header.mm-panel__head");
      const later =
        inCardHeader && this !== this.parentElement?.firstElementChild;
      return box(later && wrapped() ? 40 : 0);
    },
  );
}

const cardHeader = () =>
  screen.getByTestId("logs-card").querySelector("header") as HTMLElement;
const inHeader = (selector: string) =>
  document.querySelector(`.mm-history-controls ${selector}`);

describe("where Logs' filters go when the title line is short of room", () => {
  it("keeps every filter on the title line when it has the room", async () => {
    chipsOverflowWhile(() => false);
    renderLog();

    expect(await screen.findByTestId("logs-category-picker")).toBe(
      inHeader('[data-testid="logs-category-picker"]'),
    );
    expect(screen.queryByTestId("logs-toolbar")).not.toBeInTheDocument();
  });

  it("moves the pickers into the Log card's header, between its summary and its buttons", async () => {
    chipsOverflowWhile(() => inHeader(".mm-history-scope") !== null);
    cardHeaderWrapsWhile(() => false);
    renderLog();

    const picker = await screen.findByTestId("logs-category-picker");
    const [, summary, aside] = Array.from(cardHeader().children);
    expect(summary).toContainElement(screen.getByTestId("log-summary"));
    expect(aside).toContainElement(picker);
    expect(aside).toContainElement(
      screen.getByRole("button", { name: "Refine" }),
    );
    expect(
      picker.compareDocumentPosition(
        screen.getByRole("button", { name: "Refine" }),
      ) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
    expect(screen.queryByTestId("logs-toolbar")).not.toBeInTheDocument();
  });

  it("uses a row above the list only when the card's header cannot hold the pickers", async () => {
    chipsOverflowWhile(() => inHeader(".mm-history-scope") !== null);
    cardHeaderWrapsWhile(
      () => cardHeader().querySelector(".mm-history-scope") !== null,
    );
    renderLog();

    const picker = await screen.findByTestId("logs-category-picker");
    const row = screen.getByTestId("logs-toolbar");
    expect(row).toContainElement(picker);
    expect(row).toContainElement(screen.getByTestId("logs-level-picker"));
    expect(cardHeader()).not.toContainElement(picker);
  });

  it("keeps the level picker with the other pickers wherever they are", async () => {
    chipsOverflowWhile(() => inHeader(".mm-history-scope") !== null);
    cardHeaderWrapsWhile(() => false);
    renderLog();

    const level = await screen.findByTestId("logs-level-picker");
    expect(cardHeader()).toContainElement(level);
    expect(level.closest(".mm-history-scope")).toBe(
      screen.getByTestId("logs-category-picker").closest(".mm-history-scope"),
    );
  });
});
