/**
 * The Activity stream the web app listens to (`GET /api/v1/activity/stream`): an `activity.latest` frame whenever
 * something is written, a `processing.progress` frame carrying every running file's progress about once a second, a
 * `connection.activity` frame whenever Weir talks to a media manager or download client or one talks to Weir, a
 * `system.stats` frame every second with the machine's newest reading, a `system.tasks` frame whenever a periodic task
 * starts or ends, a `system.log` frame for each new warning or error, and keepalives. The frame shapes are the
 * server's (ActivityEndpoints, ActivityProgressFrames, ConnectionActivityHub).
 */
import { logFrameFor } from "./api/log-lines.mjs";
import { statsFrame } from "./machine/wire.mjs";

const RETRY_MS = 3000;
/** The most often a progress frame goes out, however fast a pass reports. */
export const PROGRESS_INTERVAL_MS = 1000;
const KEEPALIVE_MS = 15_000;

const latestFrame = ({ latestEventId, revision }) =>
  `event: activity.latest\ndata: ${JSON.stringify({ latest_event_id: latestEventId, activity_revision: revision })}\n\n`;
const progressFrame = (files) =>
  `event: processing.progress\ndata: ${JSON.stringify({ files })}\n\n`;
const connectionFrame = (frame) =>
  `event: connection.activity\ndata: ${JSON.stringify(frame)}\n\n`;
const namedFrame = (name, data) =>
  `event: ${name}\ndata: ${JSON.stringify(data)}\n\n`;

export class SseHub {
  #sim;
  #clients = new Set();
  #timers = [];
  #unsubscribe = [];
  #lastProgress = "";

  /** @param {import("./sim.mjs").Sim} sim */
  constructor(sim) {
    this.#sim = sim;
  }

  /** Begins following the engine and sending frames to whoever is connected. */
  start() {
    const { engine, machine, tasks } = this.#sim;
    this.#unsubscribe.push(
      engine.onChange(() => this.#broadcast(latestFrame(this.#position()))),
      engine.connections.activity.onFrame((frame) =>
        this.#broadcast(connectionFrame(frame)),
      ),
      machine.onSample((sample) =>
        this.#broadcast(namedFrame("system.stats", statsFrame(sample))),
      ),
      tasks.onChange((rows) =>
        this.#broadcast(namedFrame("system.tasks", rows)),
      ),
      engine.activity.onRecord((event) => {
        const frame = logFrameFor(event);
        if (frame) this.#broadcast(namedFrame("system.log", frame));
      }),
    );
    this.#timers.push(
      setInterval(() => this.#sendProgress(), PROGRESS_INTERVAL_MS),
    );
    this.#timers.push(
      setInterval(() => this.#broadcast(": keepalive\n\n"), KEEPALIVE_MS),
    );
  }

  stop() {
    this.#unsubscribe.forEach((stop) => stop());
    this.#unsubscribe = [];
    this.#timers.forEach(clearInterval);
    for (const response of this.#clients) response.end();
    this.#clients.clear();
  }

  #position() {
    return {
      latestEventId: this.#sim.engine.activity.latestId(),
      revision: this.#sim.engine.revision,
    };
  }

  /** Opens a stream for one browser tab. @param {import("node:http").IncomingMessage} request @param {import("node:http").ServerResponse} response */
  open(request, response) {
    response.writeHead(200, {
      "Content-Type": "text/event-stream; charset=utf-8",
      "Cache-Control": "no-store, no-cache",
      Connection: "keep-alive",
      "X-Accel-Buffering": "no",
    });
    response.write(
      `retry: ${RETRY_MS}\n\n${latestFrame(this.#position())}${progressFrame(this.#sim.engine.liveProgress(this.#sim.now()))}`,
    );
    this.#clients.add(response);
    this.#sim.machine.browserOpened();
    request.on("close", () => {
      this.#clients.delete(response);
      this.#sim.machine.browserClosed();
    });
  }

  #broadcast(chunk) {
    for (const response of this.#clients) response.write(chunk);
  }

  /** The whole set of running files, sent only when it differs from the last one sent. */
  #sendProgress() {
    if (this.#clients.size === 0) return;
    const frame = progressFrame(this.#sim.engine.liveProgress(this.#sim.now()));
    if (frame === this.#lastProgress) return;
    this.#lastProgress = frame;
    this.#broadcast(frame);
  }
}
