/** Weir's Activity record: the entries the Processing and System screens read, newest last. */

const FIRST_EVENT_ID = 1000;
/** The newest entries kept; the real record is trimmed by retention in the same way. */
const KEPT_EVENTS = 2000;

/**
 * @typedef {object} ActivityEvent
 * @property {number} id
 * @property {number} createdAt
 * @property {string} type
 * @property {string} title
 * @property {Record<string, unknown>} detail
 * @property {number | null} libraryId
 * @property {string | null} relativePath
 * @property {string} result One of success, skipped, warning, retrying, running, failed.
 * @property {string} trigger
 */

export class ActivityLog {
  /** @type {ActivityEvent[]} */
  #events = [];
  #nextId = FIRST_EVENT_ID;

  /**
   * @param {{ type: string, title: string, detail?: Record<string, unknown>, libraryId?: number | null, relativePath?: string | null, result?: string, trigger?: string }} entry
   * @param {number} nowMs
   * @returns {ActivityEvent}
   */
  record(entry, nowMs) {
    const event = {
      id: this.#nextId++,
      createdAt: nowMs,
      type: entry.type,
      title: entry.title,
      detail: entry.detail ?? {},
      libraryId: entry.libraryId ?? null,
      relativePath: entry.relativePath ?? null,
      result: entry.result ?? "success",
      trigger: entry.trigger ?? "worker",
    };
    this.#events.push(event);
    if (this.#events.length > KEPT_EVENTS) this.#events.shift();
    return event;
  }

  /** Every entry, oldest first. */
  all() {
    return this.#events;
  }

  /** The newest entry's id, or the id before the first when the record is empty. */
  latestId() {
    return this.#events.at(-1)?.id ?? FIRST_EVENT_ID - 1;
  }

  /** @param {(event: ActivityEvent) => boolean} keep */
  retain(keep) {
    this.#events = this.#events.filter(keep);
  }
}
