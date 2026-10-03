/** Turns one request into an answer: a hand-written route, else the contract's empty answer, else a refusal. */
import { answerFromContract } from "./fallback.mjs";
import { Reply } from "./reply.mjs";

const NOT_SIGNED_IN = 401;
const NOT_FOUND = 404;
const SERVER_ERROR = 500;

/** Paths a signed-out browser may still use. */
const OPEN_PATHS = [/^\/api\/v1\/auth\//, /^\/ready$/, /^\/health$/];

/** How a request was answered, for the console. */
export const ANSWER = Object.freeze({
  ROUTE: "route",
  CONTRACT: "contract",
  UNKNOWN: "unknown",
  FAILED: "failed",
});

const isApiPath = (pathname) => pathname.startsWith("/api/");

function asReply(result) {
  if (result instanceof Reply) return result;
  return result === undefined ? new Reply(204) : new Reply(200, result);
}

/**
 * @param {import("../sim.mjs").Sim} sim
 * @param {import("./router.mjs").Router} router
 * @param {{ method: string, url: string, body: Record<string, any> }} request
 * @param {(how: string, method: string, pathname: string, error?: Error) => void} [observe] Told how each request was answered.
 * @returns {Reply}
 */
export function dispatch(sim, router, request, observe = () => {}) {
  const url = new URL(request.url, "http://sim.local");
  const { pathname } = url;
  const signedOutRefusal =
    isApiPath(pathname) &&
    !sim.store.session.signedIn &&
    !OPEN_PATHS.some((open) => open.test(pathname));
  if (signedOutRefusal)
    return new Reply(NOT_SIGNED_IN, { detail: "You are not signed in." });

  const found = router.match(request.method, pathname);
  if (found) {
    observe(ANSWER.ROUTE, request.method, pathname);
    try {
      return asReply(
        found.handler({
          params: found.params,
          query: url.searchParams,
          body: request.body,
          sim,
        }),
      );
    } catch (error) {
      observe(ANSWER.FAILED, request.method, pathname, error);
      return new Reply(SERVER_ERROR, {
        detail: "The simulation could not answer this request.",
      });
    }
  }
  const fromContract = answerFromContract(
    request.method,
    pathname,
    request.body,
  );
  if (fromContract) {
    observe(ANSWER.CONTRACT, request.method, pathname);
    return fromContract;
  }
  observe(ANSWER.UNKNOWN, request.method, pathname);
  return new Reply(NOT_FOUND, {
    detail: "The simulation has no answer for this path.",
  });
}
