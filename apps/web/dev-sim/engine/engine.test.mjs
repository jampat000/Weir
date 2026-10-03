// @vitest-environment node
import { describe, expect, it } from "vitest";

import { createTestSim } from "../test-support.mjs";
import { STATUS } from "./file.mjs";
import { JOB_KIND, JOB_STATUS } from "./jobs.mjs";
import { VERDICT } from "./plan.mjs";
import { SECOND_MS, MINUTE_MS } from "../wire-time.mjs";

const filesIn = (sim, status) =>
  [...sim.engine.files.values()].filter((file) => file.status === status);
const stageNames = () => [
  "checking",
  "planning",
  "writing",
  "verifying",
  "handing_back",
];

/** Admits one download that will be cleaned, and lets it through the hold so it is waiting. */
function admitCleanFile(sim, clock) {
  const library = sim.store.libraries[0];
  const file = sim.engine.admit(library, clock.now(), {
    verdict: VERDICT.CLEAN,
  });
  Object.assign(file, {
    status: STATUS.WAITING,
    holdUntil: null,
    priority: clock.now(),
  });
  return file;
}

describe("a download arriving in a watched folder", () => {
  it("is held until it has stopped changing, then waits for a worker", () => {
    const { sim, clock, advance } = createTestSim();
    const file = sim.engine.admit(sim.store.libraries[0], clock.now(), {
      verdict: VERDICT.CLEAN,
    });
    const heldOnArrival = [STATUS.ON_HOLD, STATUS.BLOCKED_UPSTREAM].includes(
      file.status,
    );

    advance(5 * SECOND_MS);
    const stillHeld = [STATUS.ON_HOLD, STATUS.BLOCKED_UPSTREAM].includes(
      file.status,
    );
    advance(2 * MINUTE_MS);

    expect(heldOnArrival).toBe(true);
    expect(stillHeld).toBe(true);
    expect([STATUS.ON_HOLD, STATUS.BLOCKED_UPSTREAM]).not.toContain(
      file.status,
    );
  });
});

describe("a file worked on by a pass", () => {
  it("goes through checking, planning, writing, verifying and handing back in that order", () => {
    const { sim, clock, advance } = createTestSim();
    const file = admitCleanFile(sim, clock);
    const seen = [];

    for (let second = 0; second < 120; second += 1) {
      advance(SECOND_MS);
      const stage = sim.engine
        .liveProgress(clock.now())
        .find((entry) => entry.relative_path === file.relativePath)?.stage;
      if (stage && seen.at(-1) !== stage) seen.push(stage);
    }

    expect(seen).toEqual(stageNames());
  });

  it("finishes processed with a smaller saved copy, a hand-back and an Activity entry", () => {
    const { sim, clock, advance } = createTestSim();
    const file = admitCleanFile(sim, clock);

    advance(2 * MINUTE_MS);

    expect(file.status).toBe(STATUS.PROCESSED);
    expect(file.plan.outputBytes).toBeLessThan(file.sizeBytes);
    expect(file.handback.output_path).toContain(
      sim.store.libraries[0].output_folder,
    );
    expect(
      sim.engine.activity
        .all()
        .some(
          (event) =>
            event.type === "processing.file_remux_pass_completed" &&
            event.relativePath === file.relativePath,
        ),
    ).toBe(true);
  });

  it("is reported by the media manager as imported shortly after it is handed back", () => {
    const { sim, clock, advance } = createTestSim();
    const file = admitCleanFile(sim, clock);

    advance(3 * MINUTE_MS);

    expect(file.handback.outcome).toBe("imported");
    expect(file.handback.outcome_by).toBe("Radarr");
  });

  it("is rejected by the rules when it has no English audio, and waits for a person", () => {
    const { sim, clock, advance } = createTestSim();
    const file = sim.engine.admit(sim.store.libraries[0], clock.now(), {
      verdict: VERDICT.REJECTED,
    });
    Object.assign(file, { status: STATUS.WAITING, holdUntil: null });

    advance(MINUTE_MS);

    expect(file.status).toBe(STATUS.REJECTED);
    expect(file.statusReason).toContain(
      "None of its audio tracks are in English",
    );
    expect(file.statusReason).toContain('the "Movies" rules keep only English');
  });

  it("fails part-way through writing and waits for a person", () => {
    const { sim, clock, advance } = createTestSim();
    const file = sim.engine.admit(sim.store.libraries[0], clock.now(), {
      verdict: VERDICT.FAILS,
    });
    Object.assign(file, { status: STATUS.WAITING, holdUntil: null });

    advance(2 * MINUTE_MS);

    expect(file.status).toBe(STATUS.FAILED);
    expect(sim.engine.jobs.get(file.jobId).status).toBe(JOB_STATUS.FAILED);
  });

  it("is worked on again, and succeeds, once a person asks for it to be requeued", () => {
    const { sim, clock, advance } = createTestSim();
    const file = sim.engine.admit(sim.store.libraries[0], clock.now(), {
      verdict: VERDICT.REJECTED,
    });
    Object.assign(file, { status: STATUS.WAITING, holdUntil: null });
    advance(MINUTE_MS);

    sim.engine.requeue(file.id, clock.now());
    advance(2 * MINUTE_MS);

    expect(file.status).toBe(STATUS.PROCESSED);
  });
});

describe("files at once", () => {
  it("never has more files being worked on than the setting allows", () => {
    const { sim, clock, advance } = createTestSim();
    sim.store.operator.max_concurrent_files = 1;
    for (let index = 0; index < 5; index += 1) admitCleanFile(sim, clock);
    let most = 0;

    for (let second = 0; second < 120; second += 1) {
      advance(SECOND_MS);
      most = Math.max(most, sim.engine.running());
    }

    expect(most).toBe(1);
  });

  it("starts more files when the setting is raised", () => {
    const { sim, clock, advance } = createTestSim();
    sim.store.operator.max_concurrent_files = 1;
    for (let index = 0; index < 5; index += 1) admitCleanFile(sim, clock);
    advance(5 * SECOND_MS);

    sim.store.operator.max_concurrent_files = 3;
    advance(2 * SECOND_MS);

    expect(sim.engine.running()).toBe(3);
  });
});

describe("pause", () => {
  it("lets files already being written finish but starts nothing new", () => {
    const { sim, clock, advance } = createTestSim();
    const first = admitCleanFile(sim, clock);
    advance(5 * SECOND_MS);
    sim.engine.setPause({ paused: true }, clock.now());
    const later = admitCleanFile(sim, clock);

    advance(2 * MINUTE_MS);

    expect(first.status).toBe(STATUS.PROCESSED);
    expect(later.status).toBe(STATUS.WAITING);
  });

  it("starts the waiting files again when resumed", () => {
    const { sim, clock, advance } = createTestSim();
    sim.engine.setPause({ paused: true }, clock.now());
    const waiting = admitCleanFile(sim, clock);
    advance(10 * SECOND_MS);

    sim.engine.setPause({ paused: false }, clock.now());
    advance(2 * MINUTE_MS);

    expect(waiting.status).toBe(STATUS.PROCESSED);
  });

  it("lifts itself when a timed pause runs out", () => {
    const { sim, clock, advance } = createTestSim();

    sim.engine.setPause({ paused: true, pauseForMinutes: 5 }, clock.now());
    advance(6 * MINUTE_MS);

    expect(sim.engine.pause.paused).toBe(false);
  });
});

describe("library cleans", () => {
  it("are queued now and then, run in a worker slot, and mark the library file cleaned", () => {
    const { sim, advance } = createTestSim();
    const before = sim.engine.cleanRecords.length;

    advance(10 * MINUTE_MS);

    expect(sim.engine.cleanRecords.length).toBeGreaterThan(before);
    expect(
      sim.engine.jobs
        .all()
        .some(
          (job) =>
            job.kind === JOB_KIND.LIBRARY_CLEAN &&
            job.status === JOB_STATUS.COMPLETED,
        ),
    ).toBe(true);
  });

  it("are not started on their own while paused", () => {
    const { sim, clock, advance } = createTestSim();
    sim.engine.setPause({ paused: true }, clock.now());
    const before = sim.engine.jobs
      .all()
      .filter((job) => job.kind === JOB_KIND.LIBRARY_CLEAN).length;

    advance(10 * MINUTE_MS);

    expect(
      sim.engine.jobs.all().filter((job) => job.kind === JOB_KIND.LIBRARY_CLEAN)
        .length,
    ).toBe(before);
  });
});

describe("the simulation's pace", () => {
  it("runs a pass faster when the speed is raised", () => {
    const slow = createTestSim({ speed: 1 });
    const fast = createTestSim({ speed: 8 });
    const slowFile = admitCleanFile(slow.sim, slow.clock);
    const fastFile = admitCleanFile(fast.sim, fast.clock);

    slow.advance(10 * SECOND_MS);
    fast.advance(10 * SECOND_MS);

    expect(slowFile.status).not.toBe(STATUS.PROCESSED);
    expect(fastFile.status).toBe(STATUS.PROCESSED);
  });
});

describe("a session started from the same seed", () => {
  it("brings the same files in the same order", () => {
    const first = createTestSim({ seed: 11 });
    const second = createTestSim({ seed: 11 });

    first.advance(5 * MINUTE_MS);
    second.advance(5 * MINUTE_MS);

    const names = (run) =>
      [...run.sim.engine.files.values()].map((file) => file.relativePath);
    expect(names(first)).toEqual(names(second));
  });
});

describe("the board a fresh session opens on", () => {
  it("has files waiting, working and held, a failure and a rejection, and a history behind it", () => {
    const { sim } = createTestSim({ withHistory: true });

    const counts = Object.fromEntries(
      Object.values(STATUS).map((status) => [
        status,
        filesIn(sim, status).length,
      ]),
    );

    expect(counts[STATUS.PROCESSING]).toBeGreaterThanOrEqual(2);
    expect(counts[STATUS.WAITING]).toBeGreaterThan(0);
    expect(counts[STATUS.ON_HOLD]).toBeGreaterThan(0);
    expect(counts[STATUS.BLOCKED_UPSTREAM]).toBeGreaterThan(0);
    expect(counts[STATUS.FAILED]).toBeGreaterThan(0);
    expect(counts[STATUS.REJECTED]).toBeGreaterThan(0);
    expect(counts[STATUS.PROCESSED]).toBeGreaterThan(30);
  });
});
