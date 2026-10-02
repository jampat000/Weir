// @vitest-environment node
import http from "node:http";

import { afterEach, describe, expect, it } from "vitest";

import { startSimServer } from "./server.mjs";
import { createSim } from "./sim.mjs";
import { schemaNamed } from "./openapi/spec.mjs";
import { violations } from "./openapi/validate.mjs";
import { fakeClock } from "./test-support.mjs";
import { SECOND_MS } from "./wire-time.mjs";

// The app's own test setup stubs `fetch` to keep component tests off the network, so these talk to the server with
// node:http.
let running = null;
const open = [];

afterEach(async () => {
  open.splice(0).forEach((request) => request.destroy());
  await running?.stop();
  running = null;
});

async function start(sim = createSim()) {
  running = await startSimServer(sim, { log: () => {} });
  return running.port;
}

function send(port, path, { method = "GET", body } = {}) {
  return new Promise((resolve, reject) => {
    const request = http.request(
      {
        host: "127.0.0.1",
        port,
        path,
        method,
        headers: { "Content-Type": "application/json" },
      },
      (response) => {
        let text = "";
        response.on("data", (chunk) => (text += chunk));
        response.on("end", () =>
          resolve({
            status: response.statusCode,
            json: text ? JSON.parse(text) : null,
          }),
        );
      },
    );
    request.on("error", reject);
    request.end(body ? JSON.stringify(body) : undefined);
  });
}

/** Reads the stream until it has said everything in `wanted`; `onOpen` runs once the stream has begun to answer. */
function readStream(port, wanted, onOpen = () => {}) {
  return new Promise((resolve, reject) => {
    const request = http.get(
      { host: "127.0.0.1", port, path: "/api/v1/activity/stream" },
      (response) => {
        let text = "";
        let opened = false;
        response.on("data", (chunk) => {
          text += chunk;
          if (!opened) {
            opened = true;
            onOpen();
          }
          if (wanted.every((frame) => text.includes(frame)))
            resolve({ contentType: response.headers["content-type"], text });
        });
      },
    );
    request.on("error", reject);
    open.push(request);
  });
}

describe("the simulated server over HTTP", () => {
  it("opens the Activity stream with the latest event and every running file's progress", async () => {
    const port = await start();

    const { contentType, text } = await readStream(port, [
      "event: activity.latest",
      "event: processing.progress",
    ]);

    expect(contentType).toContain("text/event-stream");
    const progress = JSON.parse(
      /event: processing.progress\ndata: (.*)\n/.exec(text)[1],
    );
    expect(progress.files.length).toBeGreaterThan(0);
    expect(progress.files[0]).toEqual(
      expect.objectContaining({
        relative_path: expect.any(String),
        stage: expect.stringMatching(
          /^(checking|planning|writing|verifying|handing_back)$/,
        ),
      }),
    );
  });

  it("says on the stream when Weir asks a connection, with the frame the server sends", async () => {
    const port = await start();

    const { text } = await readStream(
      port,
      ["event: connection.activity"],
      () =>
        void send(port, "/api/v1/media-managers/connections/1/test", {
          method: "POST",
        }),
    );

    const frame = JSON.parse(
      /event: connection.activity\ndata: (.*)\n/.exec(text)[1],
    );
    expect(frame).toEqual({
      kind: "media_manager",
      id: 1,
      phase: "asked",
      direction: "outbound",
      at: expect.any(String),
      ms: null,
    });
  });

  describe("the System frames", () => {
    /** A server whose clock the test moves, so a frame can be asked for without waiting for one. */
    async function startWithClock() {
      const clock = fakeClock();
      const sim = createSim({ now: clock.now });
      const port = await start(sim);
      return { port, sim, move: (ms) => clock.advance(sim, ms) };
    }

    const frameOf = (text, name) =>
      JSON.parse(
        text.split(`event: ${name}\n`)[1].split("\n")[0].slice("data: ".length),
      );

    it("sends the machine's newest reading with the point that joins the history", async () => {
      const { port, move } = await startWithClock();

      const { text } = await readStream(port, ["event: system.stats"], () =>
        move(2 * SECOND_MS),
      );

      const frame = frameOf(text, "system.stats");
      expect(frame.now).toMatchObject({
        cores: 16,
        running: expect.any(Number),
        slots: expect.any(Number),
      });
      expect(violations(schemaNamed("SystemStatsFrame"), frame)).toEqual([]);
      expect(frame.point.at).toBe(frame.now.at);
      expect(frame.point.cpu_percent).toBe(frame.now.cpu_percent);
    });

    it("sends the whole list of tasks when one starts", async () => {
      const { port, sim, move } = await startWithClock();

      const { text } = await readStream(port, ["event: system.tasks"], () =>
        move(40 * SECOND_MS),
      );

      const rows = frameOf(text, "system.tasks");
      expect(rows).toHaveLength(sim.tasks.rows().length);
      expect(rows.some((task) => task.running)).toBe(true);
      expect(Object.keys(rows[0])).toEqual(Object.keys(sim.tasks.rows()[0]));
    });

    it("sends a warning or an error the moment it is written to the log, and nothing for information", async () => {
      const { port, sim } = await startWithClock();
      const at = sim.now();

      const { text } = await readStream(port, ["event: system.log"], () => {
        sim.engine.activity.record({ type: "system.note", title: "Fine" }, at);
        sim.engine.activity.record(
          {
            type: "system.disk_space_low",
            title: "D: is nearly full",
            result: "warning",
          },
          at,
        );
      });

      expect(frameOf(text, "system.log")).toEqual({
        at: "2026-03-14T12:00:00Z",
        level: "WARNING",
        message: "D: is nearly full",
      });
      expect(text.match(/event: system.log/g)).toHaveLength(1);
    });
  });

  it("answers JSON for a read and carries a write's body through", async () => {
    const port = await start();

    const before = await send(port, "/api/v1/pause");
    const paused = await send(port, "/api/v1/pause", {
      method: "PUT",
      body: { paused: true, csrf_token: "x" },
    });

    expect(before.json.paused).toBe(false);
    expect(paused.json.paused).toBe(true);
  });

  it("refuses a path that is not in the API contract", async () => {
    const port = await start();

    const reply = await send(port, "/api/v1/nothing/here");

    expect(reply.status).toBe(404);
  });
});
