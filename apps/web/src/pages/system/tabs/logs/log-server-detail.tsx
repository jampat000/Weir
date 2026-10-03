import { asNumber } from "../../../../lib/activity/detail";
import type { SystemLogRow } from "../../../../lib/system/system-log-api";
import { LOG_ACTIONS, LogFacts, LogRawText } from "./log-facts";

type ServerLine = NonNullable<SystemLogRow["server"]>;

/** An open server log line: the part of Weir that wrote it, what it was doing, and the exception when there is one. */
export function LogServerDetail({
  line,
  time,
  onRelated,
}: {
  line: ServerLine;
  /** The line's time in full, in Weir's time zone. */
  time: string;
  /** Narrows the log to a job and what mentions it. */
  onRelated: (jobId: number) => void;
}) {
  const jobId = asNumber(line.job_id);
  return (
    <>
      <LogFacts
        facts={[
          { label: "When", value: time },
          { label: "Logger", value: line.logger, mono: true },
          line.source && { label: "Source", value: line.source, mono: true },
          line.correlation_id && {
            label: "Request ID",
            value: line.correlation_id,
            mono: true,
          },
          jobId !== null && { label: "Job", value: `#${jobId}` },
        ]}
      />
      {line.detail ? <p className="mm-quiet-note">{line.detail}</p> : null}
      {line.traceback ? (
        <LogRawText label="Exception" text={line.traceback} />
      ) : null}
      {jobId !== null ? (
        <div className={LOG_ACTIONS}>
          <button
            type="button"
            className="mm-quiet-link"
            onClick={() => onRelated(jobId)}
          >
            Everything about this job →
          </button>
        </div>
      ) : null}
    </>
  );
}
