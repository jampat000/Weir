import { describe, expect, it } from "vitest";

import { EMPTY_LIBRARY_FORM, writeFrom } from "./library-form";

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
