/** The name of the frame on the Activity stream that a stream opens with. */
export const SERVER_HELLO_EVENT = "server.hello";

/**
 * The id of the run of the server in a `server.hello` frame, or null for one this app does not understand. The id is new
 * every time the server starts, so hearing a different one after a reconnect means the server restarted.
 */
export function parseServerHello(data: string): string | null {
  try {
    const parsed = JSON.parse(data) as { boot_id?: unknown };
    return typeof parsed.boot_id === "string" && parsed.boot_id !== ""
      ? parsed.boot_id
      : null;
  } catch {
    return null;
  }
}
