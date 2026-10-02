import { Link } from "react-router-dom";

import { StatTile, StatUnit } from "../../../components/panels/stat-tile";
import type { ProcessingFile } from "../../../lib/processing/files-api";
import { IN_PROGRESS_PATH } from "../pipeline/pipeline-stages";
import type { WorkingItem } from "../processing-model";
import { useFittingRows } from "./fit-rows";
import { STEP_WORDS } from "./working-words";

/** The most files the tile lists before saying how many more there are; how many show is up to its height. */
const MOST_OPERATIONS = 4;

/** Where a person changes how many files Weir works on at once, and how long a new download waits. */
const PERFORMANCE_PATH = "/settings?tab=performance";

type WorkingTileProps = {
  working: readonly WorkingItem[];
  /** The most files Weir works on at once, or null while it is not known. */
  filesAtOnce: number | null;
  /** How long a new download is left alone, when every workflow agrees. */
  waitSeconds: number | null;
  /** The band keeps its three tiles across, so the tile is as tall as the band and lists only the rows that fit. */
  across?: boolean;
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
        <button
          type="button"
          className="mm-op"
          onClick={() => onOpen(file)}
          data-fit=""
        >
          {content}
        </button>
      ) : (
        <div className="mm-op" data-fit="">
          {content}
        </div>
      )}
    </li>
  );
}

/** The files Weir is writing right now, each with its step and how far it has got. */
export function WorkingTile({
  working,
  filesAtOnce,
  waitSeconds,
  across = true,
  onOpen,
}: WorkingTileProps) {
  const [bodyRef, fits] = useFittingRows(across);
  const shown = working.slice(0, MOST_OPERATIONS);
  const more = working.length - Math.min(fits, shown.length);
  return (
    <StatTile
      label="Working on now"
      bodyRef={bodyRef}
      aside={
        <span className="mm-aside">
          {more > 0 ? (
            <span className="mm-aside__more">
              <Link
                to={IN_PROGRESS_PATH}
                aria-label={`${more.toLocaleString()} more in History`}
              >
                {more.toLocaleString()} more
              </Link>
              {" · "}
            </span>
          ) : null}
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
        <p className="mm-stat__idle">Nothing being cleaned.</p>
      ) : (
        <ul className="mm-ops" data-testid="live-working">
          {shown.map((item) => (
            <Operation key={item.key} item={item} onOpen={onOpen} />
          ))}
        </ul>
      )}
    </StatTile>
  );
}
