import { Link } from "react-router-dom";

import { StatTile, StatUnit } from "../../../components/panels/stat-tile";
import type { ProcessingFile } from "../../../lib/processing/files-api";
import type { WorkingItem } from "../processing-model";
import { STEP_WORDS } from "./working-words";

/** Where a person changes how many files Weir works on at once, and how long a new download waits. */
const PERFORMANCE_PATH = "/settings?tab=performance";

type WorkingTileProps = {
  working: readonly WorkingItem[];
  /** The most files Weir works on at once, or null while it is not known. */
  filesAtOnce: number | null;
  /** How long a new download is left alone, when every workflow agrees. */
  waitSeconds: number | null;
  onOpen: (file: ProcessingFile) => void;
};

function Operation({
  item,
  onOpen,
}: {
  item: WorkingItem;
  onOpen: (file: ProcessingFile) => void;
}) {
  const percent =
    item.percent === null
      ? null
      : Math.round(Math.min(100, Math.max(0, item.percent)));
  const content = (
    <>
      <span className="mm-op__text">
        <b>{item.name}</b>
        <span> · {STEP_WORDS[item.step]}</span>
      </span>
      <span className="mm-op__right">
        {percent === null ? "" : `${percent}%`}
      </span>
      <span className="mm-op__bar" aria-hidden="true">
        <i
          className={percent === null ? "mm-op__bar--moving" : undefined}
          style={percent === null ? undefined : { width: `${percent}%` }}
        />
      </span>
    </>
  );
  const { file } = item;
  return (
    <li>
      {file ? (
        <button type="button" className="mm-op" onClick={() => onOpen(file)}>
          {content}
        </button>
      ) : (
        <div className="mm-op">{content}</div>
      )}
    </li>
  );
}

/** The files Weir is writing right now, each with its step and how far it has got. */
export function WorkingTile({
  working,
  filesAtOnce,
  waitSeconds,
  onOpen,
}: WorkingTileProps) {
  return (
    <StatTile
      label="Working on now"
      aside={
        <span className="mm-aside">
          {waitSeconds === null ? null : (
            <span className="mm-aside__note">
              new downloads wait {waitSeconds}s ·{" "}
            </span>
          )}
          <Link to={PERFORMANCE_PATH}>Change</Link>
        </span>
      }
      figure={
        <>
          <span data-testid="live-working-count">
            {working.length.toLocaleString()}
          </span>
          <StatUnit>
            {filesAtOnce === null
              ? "at once"
              : `of ${filesAtOnce.toLocaleString()} at once`}
          </StatUnit>
        </>
      }
    >
      {working.length === 0 ? (
        <p className="mm-stat__idle">Nothing is being cleaned right now.</p>
      ) : (
        <ul className="mm-ops" data-testid="live-working">
          {working.map((item) => (
            <Operation key={item.key} item={item} onOpen={onOpen} />
          ))}
        </ul>
      )}
    </StatTile>
  );
}
