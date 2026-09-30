import { describe, expect, it } from "vitest";

import { library } from "../../pages/settings/tabs/libraries/library-test-fixtures";
import {
  writeFromProcessingLibrary,
  type ProcessingLibrary,
} from "./libraries-api";

/** What the server reports about a workflow but never accepts back: it works these out itself. */
const READ_ONLY_FIELDS: readonly (keyof ProcessingLibrary)[] = [
  "id",
  "display_order",
  "effective_max_concurrent_files",
  "manager_coverage",
  "manager_coverage_detail",
  "discovered_from_connection_id",
  "discovered_library_key",
  "active_job_count",
  "next_look_at",
  "updated_at",
];

describe("writeFromProcessingLibrary", () => {
  it("sends back every setting the server reported, so a save of one part never resets another", () => {
    const stored = library({
      remux_writer: "ffmpeg",
      min_file_size_mb: 12,
      rejected_file_action: "delete_file",
      failure_policy: "hold",
      manager_connection_ids: [4, 9],
    });

    const write: Record<string, unknown> = {
      ...writeFromProcessingLibrary(stored),
    };

    const settings = Object.keys(stored).filter(
      (key) => !READ_ONLY_FIELDS.includes(key as keyof ProcessingLibrary),
    );
    const dropped = settings.filter((key) => !(key in write));
    expect(dropped).toEqual([]);
    for (const key of settings) {
      expect(write[key], key).toEqual(stored[key as keyof ProcessingLibrary]);
    }
  });

  it("carries which tool writes the output", () => {
    const write = writeFromProcessingLibrary(
      library({ remux_writer: "ffmpeg" }),
    );

    expect(write.remux_writer).toBe("ffmpeg");
  });
});
