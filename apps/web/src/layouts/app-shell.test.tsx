import { fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AppShell } from "./app-shell";

const logoutMutate = vi.fn();
const scrollToMock = vi.fn();

vi.mock("../lib/auth/queries", () => ({
  useLogoutMutation: () => ({
    mutate: logoutMutate,
    isPending: false,
  }),
  // The shell now carries the pause control, which needs to know whether the signed-in
  // person may change it.
  useMeQuery: () => ({ data: { role: "operator" } }),
}));

vi.mock("../lib/pause/pause-queries", () => ({
  usePauseQuery: () => ({
    data: {
      paused: false,
      paused_until: null,
      scan_while_paused: true,
      reason: "",
      in_flight_policy: "Work already running finishes.",
    },
  }),
  useSavePause: () => ({ mutate: vi.fn(), isPending: false }),
}));

vi.mock("../lib/system/readiness-queries", () => ({
  useSystemReadinessQuery: () => ({
    data: {
      version: "2.1.2",
    },
  }),
}));

// The Processing entry says how many files are being written right now.
const filesAtOnce = { running: 0 };
vi.mock("../lib/processing/queries", () => ({
  useProcessingFilesAtOnceQuery: () => ({ data: filesAtOnce }),
}));

vi.mock("../lib/settings/queries", () => ({
  useAppSettingsQuery: () => ({
    data: {
      product_display_name: "Weir",
    },
  }),
}));

describe("AppShell", () => {
  beforeEach(() => {
    logoutMutate.mockReset();
    scrollToMock.mockReset();
    window.scrollTo = scrollToMock;
  });

  it("keeps only the version and sign-out controls in the sidebar footer", () => {
    render(
      <MemoryRouter initialEntries={["/"]}>
        <Routes>
          <Route path="/" element={<AppShell />}>
            <Route index element={<div>Home</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );

    expect(screen.getByText("Version 2.1.2")).toBeInTheDocument();
    expect(screen.getByTestId("sign-out")).toBeInTheDocument();
    expect(screen.queryByTestId("sidebar-support")).not.toBeInTheDocument();
    expect(screen.queryByText("Support Weir")).not.toBeInTheDocument();
    expect(screen.queryByText(/supporter licence/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/licence checks/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/feature limits/i)).not.toBeInTheDocument();
  });

  it("opens on Processing; there is no Home, Dashboard or Activity entry (3.2)", () => {
    render(
      <MemoryRouter initialEntries={["/"]}>
        <Routes>
          <Route path="/" element={<AppShell />}>
            <Route index element={<div>Main</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );

    expect(screen.getByRole("link", { name: "Processing" })).toHaveAttribute(
      "href",
      "/",
    );
    for (const retired of ["Home", "Dashboard", "Activity"]) {
      expect(
        screen.queryByRole("link", { name: retired }),
      ).not.toBeInTheDocument();
    }
  });

  it("sends every nav item to the screen its label names, and has no others", () => {
    render(
      <MemoryRouter initialEntries={["/"]}>
        <Routes>
          <Route path="/" element={<AppShell />}>
            <Route index element={<div>Main</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );

    const nav = screen.getByRole("navigation", { name: "Primary" });
    const items = within(nav)
      .getAllByRole("link")
      .map((link) => [link.textContent, link.getAttribute("href")]);

    // The whole nav, in order. A new entry has to be added here deliberately, and a label that
    // stops matching its destination fails rather than quietly misleading someone.
    expect(items).toEqual([
      ["Processing", "/"],
      ["History", "/history"],
      ["Library", "/library"],
      ["Settings", "/settings"],
      ["System", "/system"],
    ]);
  });

  it("marks only the current screen, and marks nothing on a page that is not one", () => {
    const { unmount } = render(
      <MemoryRouter initialEntries={["/library"]}>
        <Routes>
          <Route path="/" element={<AppShell />}>
            <Route index element={<div>Processing</div>} />
            <Route path="library" element={<div>Library</div>} />
            <Route path="*" element={<div>Not found</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );

    const current = () =>
      within(screen.getByRole("navigation", { name: "Primary" }))
        .getAllByRole("link")
        .filter((link) => link.getAttribute("aria-current") === "page")
        .map((link) => link.textContent);

    expect(current()).toEqual(["Library"]);
    unmount();

    // `/dashboard` is the Not found page now that 3.0.0 dropped its redirect (#585). Processing is
    // the index route, so it must not claim to be the screen you are on.
    render(
      <MemoryRouter initialEntries={["/dashboard"]}>
        <Routes>
          <Route path="/" element={<AppShell />}>
            <Route index element={<div>Home</div>} />
            <Route path="*" element={<div>Not found</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );

    expect(current()).toEqual([]);
  });

  it("shows how many files are being written beside Processing, and nothing when none are", () => {
    filesAtOnce.running = 2;
    const view = render(
      <MemoryRouter initialEntries={["/library"]}>
        <Routes>
          <Route path="/" element={<AppShell />}>
            <Route path="library" element={<div>Library</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    const live = screen.getByRole("link", { name: "Processing, 2 working" });
    expect(
      within(live).getByTestId("nav-processing-working"),
    ).toHaveTextContent("2 working");

    filesAtOnce.running = 0;
    view.rerender(
      <MemoryRouter initialEntries={["/library"]}>
        <Routes>
          <Route path="/" element={<AppShell />}>
            <Route path="library" element={<div>Library</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(screen.queryByTestId("nav-processing-working")).toBeNull();
    expect(
      screen.getByRole("link", { name: "Processing" }),
    ).toBeInTheDocument();
  });

  it("collapses to icons when asked, and expands again", () => {
    render(
      <MemoryRouter initialEntries={["/"]}>
        <Routes>
          <Route path="/" element={<AppShell />}>
            <Route index element={<div>Processing</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    const sidebar = document.getElementById("mm-primary-sidebar");
    const toggle = screen.getByTestId("sidebar-collapse");
    expect(sidebar).not.toHaveClass("mm-sidebar--collapsed");
    fireEvent.click(toggle);
    expect(sidebar).toHaveClass("mm-sidebar--collapsed");
    expect(toggle).toHaveAttribute("aria-label", "Expand navigation");
    fireEvent.click(toggle);
    expect(sidebar).not.toHaveClass("mm-sidebar--collapsed");
  });

  it("names the sidebar landmark after the product", () => {
    render(
      <MemoryRouter initialEntries={["/"]}>
        <Routes>
          <Route path="/" element={<AppShell />}>
            <Route index element={<div>Main</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );

    // It was "Product", left over from the suite this stopped being.
    expect(
      screen.getByRole("complementary", { name: "Weir" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("complementary", { name: "Product" }),
    ).not.toBeInTheDocument();
  });

  it("returns document scrolling to the top when the route changes", () => {
    render(
      <MemoryRouter initialEntries={["/"]}>
        <Routes>
          <Route path="/" element={<AppShell />}>
            <Route index element={<div>Processing page</div>} />
            <Route path="library" element={<div>Library page</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );

    fireEvent.click(screen.getByRole("link", { name: "Library" }));

    expect(screen.getByText("Library page")).toBeInTheDocument();
    expect(scrollToMock).toHaveBeenLastCalledWith(0, 0);
  });
});
