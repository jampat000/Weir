#!/usr/bin/env node
/**
 * `npm run dev:sim`: the web app against a simulated Weir, so the real screens can be watched with files flowing
 * through them and no server, account or media. Development only: it starts Vite's dev server, never a build, and
 * nothing here is imported by the app or its production bundle.
 *
 * Environment:
 *   SIM_SPEED  how many times faster than normal the simulated work runs (default 1)
 *   SIM_SEED   the seed for which files turn up and how they fare, to replay a session
 *   SIM_PORT   the web port to use; by default the first free one from 8790
 *   SIM_SCENARIO  busy (the default), quiet (little happening, to see the empty screens) or trouble (many files
 *              needing a person, and a connection that is down)
 */
import { spawn } from "node:child_process";
import net from "node:net";
import { fileURLToPath } from "node:url";

import { scenarioNamed } from "./scenarios.mjs";
import { createSim, DEFAULT_SEED } from "./sim.mjs";
import { startSimServer } from "./server.mjs";

const VITE_ENTRY = fileURLToPath(
  new URL("../node_modules/vite/bin/vite.js", import.meta.url),
);
const FIRST_WEB_PORT = 8790;
const LAST_WEB_PORT = 8890;
const LOOPBACK = "127.0.0.1";

function numberFromEnv(name, fallback) {
  const value = Number(process.env[name]);
  return Number.isFinite(value) && value > 0 ? value : fallback;
}

function canListenOn(port) {
  return new Promise((resolve) => {
    const probe = net.createServer();
    probe.once("error", () => resolve(false));
    probe.listen(port, LOOPBACK, () => probe.close(() => resolve(true)));
  });
}

async function firstFreePort() {
  for (let port = FIRST_WEB_PORT; port <= LAST_WEB_PORT; port += 1) {
    if (await canListenOn(port)) return port;
  }
  throw new Error(
    `No free port between ${FIRST_WEB_PORT} and ${LAST_WEB_PORT}. Set SIM_PORT to one.`,
  );
}

async function main() {
  if (process.env.NODE_ENV === "production") {
    console.error(
      "[dev-sim] This is a development tool and will not run with NODE_ENV=production.",
    );
    process.exit(1);
  }
  const speed = numberFromEnv("SIM_SPEED", 1);
  const scenario = scenarioNamed(process.env.SIM_SCENARIO);
  const sim = createSim({
    speed,
    seed: numberFromEnv("SIM_SEED", DEFAULT_SEED),
    scenario,
  });
  const api = await startSimServer(sim);
  const webPort = numberFromEnv("SIM_PORT", await firstFreePort());
  const target = `http://${LOOPBACK}:${api.port}`;
  console.log(
    `[dev-sim] Simulated Weir (${scenario.name}) on ${target} at ${speed}x. Open http://localhost:${webPort}`,
  );

  const vite = spawn(process.execPath, [VITE_ENTRY], {
    stdio: "inherit",
    env: {
      ...process.env,
      WEIR_DEV_WEB_PORT: String(webPort),
      // Both of these name the API Vite forwards /api to; the second wins over everything else, so a stray setting cannot point the simulation at a real server.
      WEIR_DEV_STACK_API_PROXY_TARGET: target,
      WEIR_SCREENSHOT_API_PROXY_TARGET: target,
    },
  });

  const shutdown = async (code) => {
    await api.stop();
    process.exit(code);
  };
  vite.on("exit", (code) => void shutdown(code ?? 0));
  for (const signal of ["SIGINT", "SIGTERM"]) {
    process.on(signal, () => vite.kill(signal));
  }
}

main().catch((error) => {
  console.error(`[dev-sim] ${error.message}`);
  process.exit(1);
});
