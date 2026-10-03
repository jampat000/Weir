/** The simulated Weir API as a plain HTTP server, with the Activity stream and a clock that keeps the engine moving. */
import { createServer } from "node:http";

import { ANSWER, dispatch } from "./api/dispatch.mjs";
import { buildRouter } from "./api/routes.mjs";
import { SseHub } from "./sse.mjs";

const STREAM_PATH = "/api/v1/activity/stream";
const TICK_MS = 250;
const BODY_METHODS = ["POST", "PUT", "PATCH", "DELETE"];
const HOST = "127.0.0.1";

async function readJsonBody(request) {
  if (!BODY_METHODS.includes(request.method)) return {};
  const chunks = [];
  for await (const chunk of request) chunks.push(chunk);
  const text = Buffer.concat(chunks).toString("utf8");
  try {
    return text ? JSON.parse(text) : {};
  } catch {
    return {};
  }
}

function send(response, reply) {
  const isText = reply.contentType !== "application/json";
  const headers = {
    "Content-Type": reply.contentType,
    "Cache-Control": "no-store",
  };
  if (reply.filename)
    headers["Content-Disposition"] = `attachment; filename="${reply.filename}"`;
  response.writeHead(reply.status, headers);
  if (reply.body === undefined) return response.end();
  return response.end(isText ? reply.body : JSON.stringify(reply.body));
}

/** Says once for each kind of request how it was answered, so the owner knows what the simulation is guessing at. */
function consoleObserver(log) {
  const reported = new Set();
  return (how, method, pathname, error) => {
    if (how === ANSWER.ROUTE) return;
    if (how === ANSWER.FAILED)
      return log(`[dev-sim] ${method} ${pathname} failed: ${error?.message}`);
    const key = `${how} ${method} ${pathname}`;
    if (reported.has(key)) return;
    reported.add(key);
    log(
      how === ANSWER.UNKNOWN
        ? `[dev-sim] No answer for ${method} ${pathname}; it is not in the API contract.`
        : `[dev-sim] ${method} ${pathname} answered with the contract's empty answer.`,
    );
  };
}

/**
 * Starts the simulated API.
 * @param {import("./sim.mjs").Sim} sim
 * @param {{ port?: number, log?: (line: string) => void }} [options] `port` 0 picks a free one.
 * @returns {Promise<{ port: number, stop: () => Promise<void> }>}
 */
export async function startSimServer(
  sim,
  { port = 0, log = console.log } = {},
) {
  const router = buildRouter();
  const hub = new SseHub(sim);
  const observe = consoleObserver(log);
  const server = createServer(async (request, response) => {
    const { pathname } = new URL(request.url ?? "/", "http://sim.local");
    if (
      request.method === "GET" &&
      pathname === STREAM_PATH &&
      sim.store.session.signedIn
    )
      return hub.open(request, response);
    const body = await readJsonBody(request);
    return send(
      response,
      dispatch(
        sim,
        router,
        { method: request.method ?? "GET", url: request.url ?? "/", body },
        observe,
      ),
    );
  });
  await new Promise((resolve) => server.listen(port, HOST, resolve));
  hub.start();
  const clock = setInterval(() => sim.engine.tick(sim.now()), TICK_MS);
  return {
    port: server.address().port,
    stop: () =>
      new Promise((resolve) => {
        clearInterval(clock);
        hub.stop();
        server.close(() => resolve());
        server.closeAllConnections();
      }),
  };
}
