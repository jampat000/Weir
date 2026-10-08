import { afterEach, describe, expect, it, vi } from "vitest";

import { bundleHasChanged } from "./bundle-changed";

function runWith(...sources: string[]): void {
  document.head.innerHTML = sources
    .map((src) => `<script type="module" src="${src}"></script>`)
    .join("");
}

function serve(html: string, status = 200): void {
  vi.stubGlobal(
    "fetch",
    vi.fn(async () => new Response(html, { status })),
  );
}

const page = (...sources: string[]) =>
  `<!doctype html><html><head>${sources
    .map((src) => `<script type="module" crossorigin src="${src}"></script>`)
    .join("")}</head><body></body></html>`;

afterEach(() => {
  document.head.innerHTML = "";
  vi.unstubAllGlobals();
});

describe("bundleHasChanged", () => {
  it("is false while the server serves the build the page is running", async () => {
    runWith("/assets/index-aaa.js");
    serve(page("/assets/index-aaa.js"));

    expect(await bundleHasChanged()).toBe(false);
  });

  it("is true once the server serves scripts with different names", async () => {
    runWith("/assets/index-aaa.js");
    serve(page("/assets/index-bbb.js"));

    expect(await bundleHasChanged()).toBe(true);
  });

  it("asks the server for the page itself, never from a cache", async () => {
    runWith("/assets/index-aaa.js");
    serve(page("/assets/index-aaa.js"));

    await bundleHasChanged();

    expect(fetch).toHaveBeenCalledWith("/", { cache: "no-store" });
  });

  it("is false when the server cannot say, or says something that is not the app", async () => {
    runWith("/assets/index-aaa.js");
    serve("", 502);
    expect(await bundleHasChanged()).toBe(false);

    serve("<html><body>Bad gateway</body></html>");
    expect(await bundleHasChanged()).toBe(false);

    vi.stubGlobal(
      "fetch",
      vi.fn(async () => {
        throw new TypeError("offline");
      }),
    );
    expect(await bundleHasChanged()).toBe(false);
  });
});
