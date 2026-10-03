/** What a route hands back when it needs to say more than "200 and this JSON". */
export class Reply {
  /**
   * @param {number} status
   * @param {unknown} [body] JSON, or a string when `contentType` says it is text.
   * @param {{ contentType?: string, filename?: string }} [options]
   */
  constructor(status, body = undefined, options = {}) {
    this.status = status;
    this.body = body;
    this.contentType = options.contentType ?? "application/json";
    this.filename = options.filename ?? null;
  }
}

export const noContent = () => new Reply(204);

/** The refusal the server gives for a record that is not there. @param {string} detail */
export const notFound = (detail) => new Reply(404, { detail });

/** A download: plain text with a file name. @param {string} body @param {string} filename @param {string} [contentType] */
export const download = (
  body,
  filename,
  contentType = "text/plain; charset=utf-8",
) => new Reply(200, body, { contentType, filename });
