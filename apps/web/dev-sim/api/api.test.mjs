// @vitest-environment node
import { describe, expect, it } from "vitest";

import { operations, successResponse } from "../openapi/spec.mjs";
import { violations } from "../openapi/validate.mjs";
import { createTestSim } from "../test-support.mjs";
import { SECOND_MS } from "../wire-time.mjs";
import { ANSWER, dispatch } from "./dispatch.mjs";
import { buildRouter } from "./routes.mjs";

const router = buildRouter();
const templateShape = (template) => template.replace(/\{[^}]+\}|:[^/]+/g, "{}");

function request(sim, method, url, body = {}, observe = undefined) {
  return dispatch(sim, router, { method, url, body }, observe);
}

/** A path from the contract with a real id in each parameter. */
function concretePath(template, sim) {
  const file =
    [...sim.engine.files.values()].find(
      (candidate) => candidate.finishedAt !== null,
    ) ?? [...sim.engine.files.values()][0];
  const ids = {
    file_id: file.id,
    library_id: 1,
    connection_id: 1,
    rule_set_id: 1,
    channel_id: 1,
    backup_id: 1,
    id: 1,
  };
  return template.replace(/\{([^}]+)\}/g, (_, name) =>
    String(ids[name] ?? "1"),
  );
}

/** Query strings the contract marks required. */
const REQUIRED_QUERY = {
  "/api/v1/processing/manager-setup": "media_type=movie",
  "/api/v1/download-clients/suggestions": "media_type=tv",
  "/api/v1/activity/file-history": "relative_path=x",
};

/** Answers that are not JSON, a stream, or need a secret the browser never sends. */
const NOT_JSON = [
  /\/log\/download$/,
  /\/download$/,
  /\/activity\/export$/,
  /\/logs\/download$/,
  /\/activity\/stream$/,
  /^\/api\/v1\/intake\//,
  /\/artwork\/posters\//,
];

/** Routes the simulation serves before the contract documents them; each one comes off this list once it is documented. */
const AHEAD_OF_THE_CONTRACT = [];

const routeKey = (method, template) => `${method} ${templateShape(template)}`;

describe("the simulated API against the API contract", () => {
  it("only has routes the contract describes", () => {
    const real = new Set(
      operations().map((op) => routeKey(op.method, op.template)),
    );

    const invented = router
      .routes()
      .filter(
        (route) =>
          !real.has(routeKey(route.method, route.template)) &&
          !AHEAD_OF_THE_CONTRACT.includes(
            routeKey(route.method, route.template),
          ),
      );

    expect(invented).toEqual([]);
  });

  it("does not list a route as ahead of the contract once the contract describes it", () => {
    const documented = new Set(
      operations().map((op) => routeKey(op.method, op.template)),
    );

    expect(
      AHEAD_OF_THE_CONTRACT.filter((route) => documented.has(route)),
    ).toEqual([]);
  });

  it("answers every read the contract describes in the shape the contract gives", () => {
    const { sim } = createTestSim({ withHistory: true });
    const problems = [];
    let checked = 0;

    for (const op of operations().filter(
      (candidate) => candidate.method === "GET",
    )) {
      if (NOT_JSON.some((pattern) => pattern.test(op.template))) continue;
      const path = concretePath(op.template, sim);
      const query = REQUIRED_QUERY[path];
      const reply = request(sim, "GET", query ? `${path}?${query}` : path);
      const { schema } = successResponse(op);
      checked += 1;
      if (reply.status !== 200)
        problems.push(`${op.template} answered ${reply.status}`);
      else if (schema)
        problems.push(...violations(schema, reply.body, op.template));
    }

    expect(problems).toEqual([]);
    expect(checked).toBeGreaterThan(50);
  });

  it("answers the file list with every file in the shape of a ProcessingFileOut", () => {
    const { sim } = createTestSim({ withHistory: true });
    const op = operations().find(
      (candidate) =>
        candidate.template === "/api/v1/processing/files" &&
        candidate.method === "GET",
    );

    const reply = request(sim, "GET", "/api/v1/processing/files");

    expect(reply.body.files.length).toBeGreaterThan(50);
    expect(violations(successResponse(op).schema, reply.body)).toEqual([]);
  });

  it("reports how it answered a path nobody wrote a route for", () => {
    const { sim } = createTestSim();
    const answers = [];

    request(
      sim,
      "POST",
      "/api/v1/auth/change-password",
      {},
      (how, method, path) => answers.push([how, method, path]),
    );
    request(sim, "GET", "/api/v1/not/a/real/path", {}, (how, method, path) =>
      answers.push([how, method, path]),
    );

    expect(answers).toEqual([
      [ANSWER.CONTRACT, "POST", "/api/v1/auth/change-password"],
      [ANSWER.UNKNOWN, "GET", "/api/v1/not/a/real/path"],
    ]);
  });
});

describe("signing in and out", () => {
  it("answers as an admin until sign-out, then refuses until sign-in", () => {
    const { sim } = createTestSim();

    const signedIn = request(sim, "GET", "/api/v1/auth/me");
    request(sim, "POST", "/api/v1/auth/logout");
    const afterLogout = request(sim, "GET", "/api/v1/auth/me");
    const refused = request(sim, "GET", "/api/v1/processing/files");
    request(sim, "POST", "/api/v1/auth/login", {
      username: "admin",
      password: "x",
    });

    expect(signedIn.body.user.role).toBe("admin");
    expect(afterLogout.status).toBe(401);
    expect(refused.status).toBe(401);
    expect(request(sim, "GET", "/api/v1/auth/me").status).toBe(200);
  });

  it("remembers the theme that was chosen", () => {
    const { sim } = createTestSim();

    request(sim, "POST", "/api/v1/auth/theme", { theme: "light" });

    expect(request(sim, "GET", "/api/v1/auth/me").body.user.app_theme).toBe(
      "light",
    );
  });
});

describe("pause and resume", () => {
  it("toggle the pause state and the engine follows it", () => {
    const { sim } = createTestSim();

    const paused = request(sim, "PUT", "/api/v1/pause", { paused: true });
    const readBack = request(sim, "GET", "/api/v1/pause");
    const resumed = request(sim, "PUT", "/api/v1/pause", { paused: false });

    expect(paused.body.paused).toBe(true);
    expect(readBack.body.paused).toBe(true);
    expect(sim.engine.pause.paused).toBe(false);
    expect(resumed.body.paused).toBe(false);
  });

  it("report when a timed pause lifts", () => {
    const { sim } = createTestSim();

    const reply = request(sim, "PUT", "/api/v1/pause", {
      paused: true,
      pause_for_minutes: 30,
    });

    expect(Date.parse(reply.body.paused_until) - sim.now()).toBe(
      30 * 60 * SECOND_MS,
    );
  });

  it("make the files-at-once answer say what the waiting files are waiting for", () => {
    const { sim, advance } = createTestSim({ withHistory: true });
    request(sim, "PUT", "/api/v1/pause", { paused: true });
    advance(SECOND_MS);

    const reply = request(sim, "GET", "/api/v1/processing/files-at-once");

    expect(reply.body.waiting_for).toBe("paused");
  });
});

describe("saving settings", () => {
  it("echoes a saved performance setting back and applies it to the engine", () => {
    const { sim } = createTestSim();

    const saved = request(sim, "PUT", "/api/v1/processing/operator-settings", {
      max_concurrent_files: 3,
    });

    expect(saved.body.max_concurrent_files).toBe(3);
    expect(
      request(sim, "GET", "/api/v1/processing/operator-settings").body
        .max_concurrent_files,
    ).toBe(3);
    expect(sim.engine.slots()).toBe(3);
  });

  it("creates, renames and deletes a workflow", () => {
    const { sim } = createTestSim();

    const created = request(sim, "POST", "/api/v1/processing/libraries", {
      name: "Anime",
      media_type: "tv",
      watched_folder: "D:\\Downloads\\Anime",
    });
    const renamed = request(
      sim,
      "PUT",
      `/api/v1/processing/libraries/${created.body.id}`,
      { name: "Cartoons" },
    );
    const deleted = request(
      sim,
      "DELETE",
      `/api/v1/processing/libraries/${created.body.id}`,
    );

    expect(created.body.name).toBe("Anime");
    expect(renamed.body.name).toBe("Cartoons");
    expect(deleted.status).toBe(204);
    expect(
      request(sim, "GET", "/api/v1/processing/libraries").body.map(
        (library) => library.name,
      ),
    ).toEqual(["Movies", "TV", "Kids", "4K Movies"]);
  });

  it("keeps a connection's API key out of what is read back, saying only that one is saved", () => {
    const { sim } = createTestSim();

    const created = request(sim, "POST", "/api/v1/media-managers/connections", {
      kind: "radarr",
      name: "Radarr 4K",
      base_url: "http://localhost:7879",
      api_key: "super-secret",
    });

    expect(created.body.api_key_is_saved).toBe(true);
    expect(JSON.stringify(created.body)).not.toContain("super-secret");
  });
});

describe("what a person can do to a file", () => {
  it("lists a failed file as needing attention, then queues it again", () => {
    const { sim, advance } = createTestSim({ withHistory: true });
    const failed = [...sim.engine.files.values()].find(
      (file) => file.status === "processing_failed",
    );

    const queued = request(
      sim,
      "POST",
      `/api/v1/processing/files/${failed.id}/requeue`,
    );
    advance(SECOND_MS);

    expect(queued.body.requeued).toBe(1);
    expect(sim.engine.files.get(failed.id).status).not.toBe(
      "processing_failed",
    );
  });

  it("queues every rejected file again with Process all again", () => {
    const { sim } = createTestSim({ withHistory: true });
    const before = request(
      sim,
      "GET",
      "/api/v1/processing/files/rejected/summary",
    ).body;

    const reply = request(
      sim,
      "POST",
      "/api/v1/processing/files/rejected/process-again",
      {},
    );

    expect(before.rejected).toBeGreaterThan(0);
    expect(reply.body.requeued).toBe(before.rejected);
    expect(
      request(sim, "GET", "/api/v1/processing/files/rejected/summary").body
        .rejected,
    ).toBe(0);
  });

  it("tells a finished file's story from its record", () => {
    const { sim } = createTestSim({ withHistory: true });
    const done = [...sim.engine.files.values()].find(
      (file) => file.status === "processed" && file.plan.verdict === "clean",
    );

    const log = request(
      sim,
      "GET",
      `/api/v1/processing/files/${done.id}/log`,
    ).body;

    expect(log.entries[0].story.map((step) => step.heading)).toEqual([
      "Picked up",
      "Looked inside",
      "Planned",
      "Worked",
      "Checked",
      "Handed back",
    ]);
  });

  it("removes a file from Activity and keeps it listed as kept when asked to", () => {
    const { sim } = createTestSim({ withHistory: true });
    const done = [...sim.engine.files.values()].find(
      (file) => file.status === "processed",
    );

    request(sim, "DELETE", `/api/v1/processing/files/${done.id}`, {
      resolution: "keep",
    });

    expect(
      request(sim, "GET", "/api/v1/processing/kept-files").body.files.map(
        (file) => file.id,
      ),
    ).toContain(done.id);
    expect(sim.engine.files.has(done.id)).toBe(false);
  });
});

describe("the totals Processing shows for today", () => {
  it("count what finished, and what it saved, from the files", () => {
    const { sim } = createTestSim({ withHistory: true });

    const stats = request(
      sim,
      "GET",
      "/api/v1/processing/overview-stats?window_days=1",
    ).body;

    expect(stats.files_processed).toBeGreaterThan(30);
    expect(stats.net_space_saved_bytes).toBeGreaterThan(0);
    expect(stats.files_failed).toBe(1);
  });
});
