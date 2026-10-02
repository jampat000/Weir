// @vitest-environment node
import { describe, expect, it } from "vitest";

import { release } from "../engine/seed-support.mjs";
import { VERDICT } from "../engine/plan.mjs";
import { SCENARIO, SCENARIOS } from "../scenarios.mjs";
import { createTestSim } from "../test-support.mjs";
import { MINUTE_MS, SECOND_MS } from "../wire-time.mjs";
import { DISK_SPACE_LOW } from "./drive-alerts.mjs";

const MEBIBYTE = 1024 ** 2;
const GIBIBYTE = 1024 ** 3;

const sessionOf = (name, options = {}) =>
  createTestSim({ withHistory: true, scenario: SCENARIOS[name], ...options });

/** A session where nothing is happening until the test makes it. */
const emptySession = () =>
  createTestSim({ scenario: SCENARIOS[SCENARIO.QUIET] });

const series = (sim, pick) => sim.machine.history().map(pick);
const average = (values) =>
  values.reduce((sum, value) => sum + value, 0) / values.length;
const spread = (values) => Math.max(...values) - Math.min(...values);

/** A download that has been cleaned and is waiting for a worker in a workflow. */
function waitingFile(sim, clock, libraryId) {
  const library = sim.store.libraries.find((entry) => entry.id === libraryId);
  return release(
    sim.engine.admit(library, clock.now(), { verdict: VERDICT.CLEAN }),
    { priority: clock.now() },
  );
}

const drive = (sim, name, nowMs) =>
  sim.machine.drives(nowMs).find((entry) => entry.name === name);

describe("the machine's readings when a session opens", () => {
  it.each(Object.values(SCENARIO))(
    "hold the last ten minutes, a second apart and ending now, in the %s scenario",
    (name) => {
      const { sim, clock } = sessionOf(name);

      const times = series(sim, (sample) => sample.atMs);

      expect(times).toHaveLength(600);
      expect(times.at(-1)).toBe(clock.now());
      expect(times.slice(1).map((at, index) => at - times[index])).toEqual(
        Array(599).fill(SECOND_MS),
      );
    },
  );

  it("keep only the last ten minutes as time goes on", () => {
    const { sim, advance } = sessionOf(SCENARIO.QUIET);

    advance(3 * MINUTE_MS);

    const times = series(sim, (sample) => sample.atMs);
    expect(times).toHaveLength(600);
    expect(times.at(-1) - times[0]).toBe(599 * SECOND_MS);
  });

  it("show Weir busy before the session opened in the busy scenario, and idle in the quiet one", () => {
    const busy = sessionOf(SCENARIO.BUSY).sim;
    const quiet = sessionOf(SCENARIO.QUIET).sim;

    expect(
      average(series(busy, (s) => s.processingWriteBytesPerSecond)),
    ).toBeGreaterThan(100 * MEBIBYTE);
    expect(
      Math.max(...series(quiet, (s) => s.processingWriteBytesPerSecond)),
    ).toBe(0);
  });
});

describe("the processor and the disk following the engine", () => {
  it("sit idle with nothing being written, and rise while a file is written", () => {
    const { sim, clock, advance } = emptySession();
    advance(30 * SECOND_MS);
    const idle = sim.machine.latest;
    waitingFile(sim, clock, 1);

    const writing = [];
    for (let second = 0; second < 40; second += 1) {
      advance(SECOND_MS);
      writing.push(sim.machine.latest);
    }

    const busiest = writing.reduce((best, sample) =>
      sample.processingWriteBytesPerSecond > best.processingWriteBytesPerSecond
        ? sample
        : best,
    );
    expect(idle.processingWriteBytesPerSecond).toBe(0);
    expect(busiest.processingWriteBytesPerSecond).toBeGreaterThan(
      50 * MEBIBYTE,
    );
    expect(busiest.processingReadBytesPerSecond).toBeGreaterThan(
      busiest.processingWriteBytesPerSecond,
    );
    expect(busiest.processingSpeed).toBeGreaterThan(100);
    expect(busiest.cpuPercent).toBeGreaterThan(idle.cpuPercent + 4);
    expect(busiest.diskWriteBytesPerSecond).toBeGreaterThanOrEqual(
      busiest.processingWriteBytesPerSecond,
    );
  });

  it("show the tools using more of the processor with two files being written than with one", () => {
    const oneFile = emptySession();
    const twoFiles = emptySession();
    waitingFile(oneFile.sim, oneFile.clock, 1);
    waitingFile(twoFiles.sim, twoFiles.clock, 1);
    waitingFile(twoFiles.sim, twoFiles.clock, 2);
    const toolsWhileWriting = ({ sim, advance }) => {
      const readings = [];
      for (let second = 0; second < 40; second += 1) {
        advance(SECOND_MS);
        readings.push(sim.machine.latest.toolsCpuPercent);
      }
      return Math.max(...readings);
    };

    expect(toolsWhileWriting(twoFiles)).toBeGreaterThan(
      toolsWhileWriting(oneFile) * 1.5,
    );
  });

  it("count the files Weir is working on and the places it has for them", () => {
    const { sim } = sessionOf(SCENARIO.BUSY);

    expect(sim.machine.latest.running).toBe(sim.engine.running());
    expect(sim.machine.latest.slots).toBe(sim.engine.slots());
  });
});

describe("how each scenario shapes the machine", () => {
  it("keeps the quiet scenario flat and the busy one lively", () => {
    const quiet = sessionOf(SCENARIO.QUIET).sim;
    const busy = sessionOf(SCENARIO.BUSY).sim;

    expect(spread(series(quiet, (s) => s.cpuPercent))).toBeLessThan(2);
    expect(spread(series(busy, (s) => s.cpuPercent))).toBeGreaterThan(8);
  });

  it("lets memory drift slowly, never jumping from one second to the next", () => {
    const { sim, advance } = sessionOf(SCENARIO.BUSY);
    advance(5 * MINUTE_MS);

    const memory = series(sim, (s) => s.memoryUsedBytes / s.memoryTotalBytes);

    const biggestStep = Math.max(
      ...memory.slice(1).map((share, index) => Math.abs(share - memory[index])),
    );
    expect(biggestStep).toBeLessThan(0.01);
    expect(spread(memory)).toBeLessThan(0.05);
  });

  it("brings spikes of other work in the trouble scenario, and none in the busy one", () => {
    const trouble = sessionOf(SCENARIO.TROUBLE);
    const busy = sessionOf(SCENARIO.BUSY);
    trouble.advance(20 * MINUTE_MS);
    busy.advance(20 * MINUTE_MS);

    expect(
      Math.max(...series(trouble.sim, (s) => s.cpuPercent)),
    ).toBeGreaterThan(70);
    expect(Math.max(...series(busy.sim, (s) => s.cpuPercent))).toBeLessThan(70);
  });

  it("runs the trouble scenario with memory nearly full and a reboot waiting", () => {
    const { sim } = sessionOf(SCENARIO.TROUBLE);

    const { memoryUsedBytes, memoryTotalBytes } = sim.machine.latest;
    expect(memoryUsedBytes / memoryTotalBytes).toBeGreaterThan(0.75);
    expect(sim.machine.profile.rebootPending).toBe(true);
  });
});

describe("the drives behind the workflows", () => {
  it("has one drive for each place the workflows' folders are, with the workflows and roles on it", () => {
    const { sim, clock } = sessionOf(SCENARIO.BUSY);

    const drives = sim.machine.drives(clock.now());

    expect(drives.map((entry) => entry.name)).toEqual(["D:", "E:"]);
    expect(drives[0].workflows.map((entry) => entry.name)).toEqual([
      "Movies",
      "TV",
      "Kids",
      "4K Movies",
    ]);
    expect(drives[1].workflows).toEqual([
      { id: 4, name: "4K Movies", roles: ["work"] },
    ]);
  });

  it("asks Weir to keep free what the workflows on the drive say, in bytes", () => {
    const { sim, clock } = sessionOf(SCENARIO.BUSY);
    sim.store.libraries[1].minimum_free_disk_space_mb = 50 * 1024;

    expect(drive(sim, "D:", clock.now()).keepFreeBytes).toBe(50 * GIBIBYTE);
  });

  it("starts the trouble scenario with one drive below the space it should keep free and the others well above", () => {
    const { sim, clock } = sessionOf(SCENARIO.TROUBLE);

    const [system, spare] = sim.machine.drives(clock.now());

    expect(system.freeBytes).toBeLessThan(system.keepFreeBytes);
    expect(spare.freeBytes).toBeGreaterThan(spare.keepFreeBytes * 10);
  });

  it("starts the busy scenario with every drive above the space it should keep free", () => {
    const { sim, clock } = sessionOf(SCENARIO.BUSY);

    for (const entry of sim.machine.drives(clock.now()))
      expect(entry.freeBytes).toBeGreaterThan(entry.keepFreeBytes);
  });

  it("loses free space as a cleaned copy no manager takes is written, and gets it back when a manager takes one", () => {
    const { sim, clock, advance } = createTestSim();
    advance(SECOND_MS);
    const before = drive(sim, "D:", clock.now()).freeBytes;
    const kept = waitingFile(sim, clock, 3);
    const taken = waitingFile(sim, clock, 1);

    advance(70 * SECOND_MS);
    const afterWriting = drive(sim, "D:", clock.now());
    advance(20 * SECOND_MS);

    expect(kept.handback).not.toBeNull();
    expect(taken.handback.outcome).toBe("imported");
    expect(afterWriting.weirBytes).toBeGreaterThanOrEqual(
      kept.plan.outputBytes,
    );
    expect(before - drive(sim, "D:", clock.now()).freeBytes).toBeGreaterThan(
      kept.plan.outputBytes * 0.9,
    );
    expect(before - drive(sim, "D:", clock.now()).freeBytes).toBeLessThan(
      kept.plan.outputBytes + taken.plan.outputBytes,
    );
  });

  it("says when a drive will be full only once it has been watched filling for a while", () => {
    const { sim, clock, advance } = createTestSim();
    advance(SECOND_MS);

    const early = drive(sim, "E:", clock.now()).fullInDays;
    advance(5 * MINUTE_MS);
    const later = drive(sim, "E:", clock.now()).fullInDays;

    expect(early).toBeNull();
    expect(later).toBeGreaterThan(1);
  });

  it("reads how busy a local drive is, and only the free space of a network share", () => {
    const { sim, clock } = sessionOf(SCENARIO.BUSY);
    sim.store.libraries[2].output_folder = "\\\\nas\\media\\Kids";

    const share = drive(sim, "\\\\nas\\media", clock.now());

    expect(share).toMatchObject({
      path: "\\\\nas\\media",
      readBytesPerSecond: null,
      writeBytesPerSecond: null,
      busyPercent: null,
    });
    expect(share.freeBytes).toBeGreaterThan(0);
    expect(drive(sim, "D:", clock.now()).busyPercent).not.toBeNull();
  });

  it("puts the reads on the drive a file is read from and the writes on the drive it is written to", () => {
    const { sim, clock, advance } = createTestSim();
    waitingFile(sim, clock, 4);
    let spare = null;
    let system = null;

    for (let second = 0; second < 40; second += 1) {
      advance(SECOND_MS);
      const drives = sim.machine.drives(clock.now());
      if (drives[1].writeBytesPerSecond > 0) {
        system = drives[0];
        spare = drives[1];
      }
    }

    expect(spare.writeBytesPerSecond).toBeGreaterThan(50 * MEBIBYTE);
    expect(system.readBytesPerSecond).toBeGreaterThan(50 * MEBIBYTE);
    expect(system.writeBytesPerSecond).toBe(0);
  });
});

describe("saying so in the log when a drive is short of space", () => {
  const warningsAbout = (sim) =>
    sim.engine.activity.all().filter((event) => event.type === DISK_SPACE_LOW);

  it("writes a warning that names the drive, and says it again after a while", () => {
    const { sim, advance } = sessionOf(SCENARIO.TROUBLE);

    advance(30 * SECOND_MS);
    const first = warningsAbout(sim).length;
    advance(5 * MINUTE_MS);

    expect(first).toBe(1);
    expect(warningsAbout(sim).length).toBeGreaterThan(first);
    expect(warningsAbout(sim)[0]).toMatchObject({
      result: "warning",
      title: expect.stringContaining("Free space on D:"),
    });
  });

  it("says nothing while every drive has the room it should", () => {
    const { sim, advance } = sessionOf(SCENARIO.BUSY);

    advance(5 * MINUTE_MS);

    expect(warningsAbout(sim)).toEqual([]);
  });
});
