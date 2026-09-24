/**
 * What to tell someone when a request fails: the error's own message when it has one, otherwise the
 * fallback. The server's messages are written for the operator, so they are shown as they come.
 */
export function errorMessage(error: unknown, fallback: string): string {
  if (typeof error === "string") return error.trim() ? error : fallback;
  if (error && typeof error === "object" && "message" in error) {
    const message = String((error as { message: unknown }).message ?? "");
    if (message.trim()) return message;
  }
  return fallback;
}
