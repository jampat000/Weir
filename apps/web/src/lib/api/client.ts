/**
 * Browser API client — cookie session auth with ``credentials: 'include'``.
 * No localStorage tokens; backend ``UserSession`` + cookie are authoritative.
 */

const API_PREFIX = "/api/v1";

function baseUrl(): string {
  const raw = import.meta.env.VITE_API_BASE_URL?.trim();
  return raw ? raw.replace(/\/$/, "") : "";
}

export function apiUrl(path: string): string {
  const p = path.startsWith("/") ? path : `/${path}`;
  if (!p.startsWith(API_PREFIX)) {
    throw new Error(`API paths must be under ${API_PREFIX}`);
  }
  return `${baseUrl()}${p}`;
}

export async function apiFetch(path: string, init?: RequestInit): Promise<Response> {
  return fetch(apiUrl(path), {
    ...init,
    credentials: "include",
    headers: {
      Accept: "application/json",
      ...init?.headers,
    },
  });
}

export async function readJson<T>(r: Response): Promise<T> {
  const text = await r.text();
  if (!text) {
    return undefined as T;
  }
  return JSON.parse(text) as T;
}

/**
 * FastAPI ``detail`` may be a string, a validation error array, or (rarely) a nested object.
 * Never pass ``detail`` straight into ``new Error()`` — non-strings become ``"[object Object]"``.
 */
export function apiErrorDetailToString(detail: unknown): string {
  if (detail === undefined || detail === null) {
    return "";
  }
  if (typeof detail === "string") {
    return detail;
  }
  if (Array.isArray(detail)) {
    const parts = detail.map((item) => {
      if (typeof item === "object" && item !== null && "msg" in item) {
        const m = (item as { msg?: unknown }).msg;
        if (typeof m === "string") {
          return m;
        }
      }
      try {
        return JSON.stringify(item);
      } catch {
        return String(item);
      }
    });
    return parts.filter((s) => s.length > 0).join(" ");
  }
  if (typeof detail === "object") {
    try {
      return JSON.stringify(detail);
    } catch {
      return "Request failed.";
    }
  }
  return String(detail);
}
