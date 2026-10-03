/** Sign-in and the account: the simulation is always signed in as an admin until someone signs out. */
import { shaped } from "../openapi/skeleton.mjs";
import { SIGNED_IN_USER } from "../store.mjs";
import { toWire, DAY_MS } from "../wire-time.mjs";
import { noContent, Reply } from "./reply.mjs";

export const CSRF_TOKEN = "dev-sim-csrf-token";

const SESSION_DAYS = 30;
const IDLE_MINUTES = 60 * 24 * 7;
const NOT_SIGNED_IN = 401;

function currentSession(sim) {
  return shaped("CurrentSessionOut", {
    session_id: "dev-sim-session",
    current: true,
    client_label: "This browser",
    trusted_device: true,
    absolute_timeout_days: SESSION_DAYS,
    idle_timeout_minutes: IDLE_MINUTES,
    created_at: toWire(sim.startedAt),
    last_seen_at: toWire(sim.now()),
    absolute_expires_at: toWire(sim.startedAt + SESSION_DAYS * DAY_MS),
  });
}

const me = (store) => ({
  user: { ...SIGNED_IN_USER, app_theme: store.session.theme },
});

/** @param {import("./router.mjs").Router} router */
export function registerAuthRoutes(router) {
  router.get("/api/v1/auth/csrf", () => ({ csrf_token: CSRF_TOKEN }));
  router.get("/api/v1/auth/bootstrap/status", () => ({
    bootstrap_allowed: false,
    reason: "An admin account already exists.",
    requires_setup_code: false,
  }));
  router.get("/api/v1/auth/me", ({ sim }) =>
    sim.store.session.signedIn
      ? me(sim.store)
      : new Reply(NOT_SIGNED_IN, { detail: "You are not signed in." }),
  );
  router.post("/api/v1/auth/login", ({ sim }) => {
    sim.store.session.signedIn = true;
    return me(sim.store);
  });
  router.post("/api/v1/auth/logout", ({ sim }) => {
    sim.store.session.signedIn = false;
    return noContent();
  });
  router.post("/api/v1/auth/theme", ({ sim, body }) => {
    sim.store.session.theme = body.theme;
    return { message: "Theme saved.", app_theme: body.theme };
  });
  router.get("/api/v1/auth/session", ({ sim }) => currentSession(sim));
  router.get("/api/v1/auth/sessions", ({ sim }) => ({
    items: [currentSession(sim)],
  }));
}
