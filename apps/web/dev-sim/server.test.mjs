// @vitest-environment node
import http from "node:http";

import { afterEach, describe, expect, it } from "vitest";

import { startSimServer } from "./server.mjs";
import { createSim } from "./sim.mjs";

// The app's own test setup stubs `fetch` to keep component tests off the network, so these talk to the server with
// node:http.
let running = null;
const open = [];

afterEach(async () => {
  open.splice(0).forEach((request) => request.destroy());
  await running?.stop();
  running = null;
});

async function start() {
  running = await startSimServer(createSim(), { log: () => {} });
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
