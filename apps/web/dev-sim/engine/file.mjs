/** The simulation's record of one download Weir has picked up: what it is, where it stands, what became of it. */

/** The states a download passes through, as the server names them. */
export const STATUS = Object.freeze({
  ON_HOLD: "on_hold",
  BLOCKED_UPSTREAM: "blocked_upstream",
  WAITING: "unprocessed",
  PROCESSING: "processing",
  PROCESSED: "processed",
  FAILED: "processing_failed",
  REJECTED: "rejected",
  CANCELLED: "cancelled",
  SKIPPED: "skipped",
});

/** How the server marks a rejection by the rules themselves, where no media manager was asked for another copy. */
export const REJECTED_BY_RULES = "rules";

/**
 * @typedef {object} SimFile
 * @property {number} id
 * @property {number} libraryId
 * @property {string} libraryName
 * @property {"movie" | "tv"} mediaType
 * @property {string} relativePath
 * @property {string | null} posterId The id the title's poster is served under.
 * @property {number} sizeBytes
 * @property {{ codec: string, width: number, height: number }} video
 * @property {number} durationSeconds
 * @property {string} status One of {@link STATUS}.
 * @property {string} statusReason
 * @property {string | null} failureClass
 * @property {number} failureAttempts
 * @property {number} createdAt
 * @property {number} updatedAt
 * @property {number | null} lastAttemptAt
 * @property {number | null} holdUntil
 * @property {number | null} sizeChangedAt
 * @property {string | null} blockedBy The media manager still importing it.
 * @property {import("./plan.mjs").Plan} plan
 * @property {import("./pass.mjs").Run | null} run
 * @property {number} passStartedAt
 * @property {number | null} jobId
 * @property {number} priority Lower starts sooner.
 * @property {Record<string, any> | null} handback
 * @property {number | null} handbackSettlesAt When the media manager takes the copy and says what it did.
 * @property {number | null} finishedAt
 * @property {Record<string, any> | null} outcomeDetail
 */

/**
 * @param {Partial<SimFile> & Pick<SimFile, "id" | "libraryId" | "libraryName" | "mediaType" | "relativePath" | "sizeBytes" | "plan">} fields
 * @returns {SimFile}
 */
export function createFile(fields) {
  const resolution = fields.video?.height ?? 1080;
  return {
    video: {
      codec: resolution >= 2160 ? "hevc" : "h264",
      width: Math.round((resolution * 16) / 9),
      height: resolution,
    },
    posterId: null,
    durationSeconds: 6000,
    status: STATUS.WAITING,
    statusReason: "",
    failureClass: null,
    failureAttempts: 0,
    createdAt: 0,
    updatedAt: 0,
    lastAttemptAt: null,
    holdUntil: null,
    sizeChangedAt: null,
    blockedBy: null,
    run: null,
    passStartedAt: 0,
    jobId: null,
    priority: fields.id,
    handback: null,
    handbackSettlesAt: null,
    finishedAt: null,
    outcomeDetail: null,
    ...fields,
  };
}

/** Whether the file is waiting on a person: its pass ended in a failure or a rejection. */
export const needsAttention = (file) =>
  file.status === STATUS.FAILED || file.status === STATUS.REJECTED;
