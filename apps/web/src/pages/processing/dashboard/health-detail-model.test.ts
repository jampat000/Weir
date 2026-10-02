import { describe, expect, it } from "vitest";

import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import { NO_FREE_SPACE_LIMIT, diskRows } from "./health-detail-model";

describe("the disk space each workflow keeps free", () => {
  const workflow = (megabytes: number) =>
    ({
      id: 4,
      name: "Movies",
      output_folder: "D:/clean/movies",
      minimum_free_disk_space_mb: megabytes,
    }) as ProcessingLibrary;

  it("reads the amount in the units a drive is measured in", () => {
    expect(diskRows([workflow(10_240)])).toEqual([
      {
        key: 4,
        workflow: "Movies",
        outputFolder: "D:/clean/movies",
        keepFree: "10.00 GB",
      },
    ]);
  });

  it("says there is no limit when the check is off", () => {
    expect(diskRows([workflow(0)])[0].keepFree).toBe(NO_FREE_SPACE_LIMIT);
  });
});
