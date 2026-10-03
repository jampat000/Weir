/**
 * What a route handler is given.
 * @typedef {object} RouteContext
 * @property {Record<string, string>} params Path parameters, such as `id` from `/files/:id`.
 * @property {URLSearchParams} query
 * @property {Record<string, any>} body The JSON body of a write, or an empty object.
 * @property {import("../sim.mjs").Sim} sim
 */

/** @typedef {(context: RouteContext) => unknown} Handler */

function compile(template) {
  const keys = [];
  const source = template
    .split("/")
    .map((part) => {
      if (!part.startsWith(":"))
        return part.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
      keys.push(part.slice(1));
      return "([^/]+)";
    })
    .join("/");
  return { pattern: new RegExp(`^${source}$`), keys };
}

/** A small route table: a method and a path with `:name` parameters pick a handler. */
export class Router {
  #routes = [];

  /**
   * @param {string} method
   * @param {string} template
   * @param {Handler} handler
   */
  add(method, template, handler) {
    this.#routes.push({ method, template, ...compile(template), handler });
  }

  get(template, handler) {
    this.add("GET", template, handler);
  }

  post(template, handler) {
    this.add("POST", template, handler);
  }

  put(template, handler) {
    this.add("PUT", template, handler);
  }

  delete(template, handler) {
    this.add("DELETE", template, handler);
  }

  /** Every route registered, as the method and path template it was added with. */
  routes() {
    return this.#routes.map(({ method, template }) => ({ method, template }));
  }

  /**
   * The handler for a request and the path parameters it captured, or null.
   * Routes added first win, so a literal path registered before a parameterised one takes precedence.
   * @param {string} method
   * @param {string} pathname
   */
  match(method, pathname) {
    for (const route of this.#routes) {
      if (route.method !== method) continue;
      const found = route.pattern.exec(pathname);
      if (!found) continue;
      const params = Object.fromEntries(
        route.keys.map((key, index) => [
          key,
          decodeURIComponent(found[index + 1]),
        ]),
      );
      return { handler: route.handler, params };
    }
    return null;
  }
}
