import { describe, expect, it } from "vitest";

import {
  STEP_WORDS,
  moreWords,
  sharedWaitSeconds,
  waitWords,
} from "./working-words";

const workflow = (enabled: boolean, ready_after_seconds: number) => ({
  enabled,
  ready_after_seconds,
});

describe("how long a new download waits", () => {
  it("is the wait every workflow that is switched on agrees on", () => {
    expect(sharedWaitSeconds([workflow(true, 60), workflow(true, 60)])).toBe(
      60,
    );
  });

  it("ignores a workflow that is switched off", () => {
    expect(sharedWaitSeconds([workflow(true, 60), workflow(false, 5)])).toBe(
      60,
    );
  });

  it("is not one number when the workflows differ", () => {
    expect(
      sharedWaitSeconds([workflow(true, 60), workflow(true, 10)]),
    ).toBeNull();
  });

  it("is not a number when nothing waits or nothing is on", () => {
    expect(sharedWaitSeconds([workflow(true, 0)])).toBeNull();
    expect(sharedWaitSeconds([workflow(false, 60)])).toBeNull();
    expect(sharedWaitSeconds([])).toBeNull();
  });
});

describe("the step a pass is on, in words", () => {
  it("names every step as an action", () => {
    expect(Object.values(STEP_WORDS)).toEqual([
      "Checking",
      "Planning",
      "Writing",
      "Verifying",
      "Handing back",
    ]);
  });
});

describe("what the wait for a new download says", () => {
  it("says it whole, then in fewer words for a tile too narrow for it", () => {
    expect(waitWords(60)).toEqual(["new downloads wait 60s", "wait 60s"]);
  });
});

describe("what the count of files beyond the tile's rows says", () => {
  it("says it whole, then as a bare plus, then nothing where not even that fits", () => {
    expect(moreWords(3)).toEqual(["3 more", "+3", ""]);
    expect(moreWords(1_200)).toEqual(["1,200 more", "+1,200", ""]);
  });
});
