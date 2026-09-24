import { fetchCsrfToken } from "./auth-api";
import { apiFetch, requireOk } from "./client";

type Method = "POST" | "PUT" | "PATCH" | "DELETE";

/**
 * A state-changing request with a fresh CSRF token in its JSON body, answered as it comes back, for a
 * caller that reads a refusal itself.
 */
export async function sendJsonUnchecked(
  path: string,
  method: Method,
  body: object,
): Promise<Response> {
  const csrf_token = await fetchCsrfToken();
  return apiFetch(path, {
    method,
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ ...body, csrf_token }),
  });
}

/**
 * A state-changing request with a fresh CSRF token in its JSON body. A refusal throws with the
 * server's reason, or `failure` when it gives none.
 */
export async function sendJson(
  path: string,
  method: Method,
  body: object,
  failure: string,
): Promise<Response> {
  const response = await sendJsonUnchecked(path, method, body);
  await requireOk(path, response, failure);
  return response;
}
