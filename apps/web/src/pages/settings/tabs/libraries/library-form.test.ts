import { describe, expect, it } from "vitest";

import { EMPTY_LIBRARY_FORM, formFrom, writeFrom } from "./library-form";
import { library } from "./library-test-fixtures";

describe("writeFrom", () => {
  it("sends a blank or unreadable number as the server's default", () => {
    const write = writeFrom({
      ...EMPTY_LIBRARY_FORM,
      name: " Movies ",
      max_attempts: "",
      retry_backoff_seconds: "soon",
    });

    expect(write.name).toBe("Movies");
    expect(write.max_attempts).toBe(3);
    expect(write.retry_backoff_seconds).toBe(300);
  });

  it("sends a blank minimum size or wait as using the Performance setting", () => {
    const write = writeFrom({
      ...EMPTY_LIBRARY_FORM,
      min_file_size_mb: "",
      min_file_age_seconds: "  ",
    });

    expect(write.min_file_size_mb).toBeNull();
    expect(write.min_file_age_seconds).toBeNull();
  });

  it("sends a minimum size or wait of zero as the library's own choice", () => {
    const write = writeFrom({
      ...EMPTY_LIBRARY_FORM,
      min_file_size_mb: "0",
      min_file_age_seconds: "0",
    });

    expect(write.min_file_size_mb).toBe(0);
    expect(write.min_file_age_seconds).toBe(0);
  });

  it("sends a blank date window side as no limit", () => {
    const write = writeFrom({ ...EMPTY_LIBRARY_FORM, created_after: "  " });

    expect(write.created_after).toBeNull();
  });

  it("keeps a second manager link the editor does not show", () => {
    const write = writeFrom(
      { ...EMPTY_LIBRARY_FORM, manager_connection_id: "4" },
      {
        manager_connection_ids: [2, 9],
      } as Parameters<typeof writeFrom>[1],
    );

    expect(write.manager_connection_ids).toEqual([4, 9]);
  });
});

describe("formFrom", () => {
  it("leaves the minimum size and wait blank for a library that uses the Performance setting", () => {
    const form = formFrom(library());

    expect(form.min_file_size_mb).toBe("");
    expect(form.min_file_age_seconds).toBe("");
  });

  it("shows a library's own minimum size and wait, including zero", () => {
    const form = formFrom(
      library({ min_file_size_mb: 200, min_file_age_seconds: 0 }),
    );

    expect(form.min_file_size_mb).toBe("200");
    expect(form.min_file_age_seconds).toBe("0");
  });
});
