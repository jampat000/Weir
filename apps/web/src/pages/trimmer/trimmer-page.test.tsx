import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { TrimmerPage } from "./trimmer-page";

describe("TrimmerPage", () => {
  it("names shipped job kinds honestly", () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { container } = render(
      <QueryClientProvider client={qc}>
        <MemoryRouter>
          <TrimmerPage />
        </MemoryRouter>
      </QueryClientProvider>,
    );
    const page = container.querySelector(".mm-page");
    expect(page).toBeTruthy();
    const t = page!.textContent ?? "";
    expect(t).toMatch(/trimmer\.trim_plan\.constraints_check\.v1/);
    expect(t).toMatch(/trimmer\.supplied_trim_plan\.json_file_write\.v1/);
    expect(t).toMatch(/MEDIAMOP_TRIMMER_WORKER_COUNT/);
    expect(t).not.toMatch(/trimmer\.source_segment\.ffmpeg_extract\.v1/);
  });
});
