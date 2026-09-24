import { formatBytes } from "../../lib/format/bytes";
import type { ProcessingFile } from "../../lib/processing/files-api";
import { tookWords, type DetailSizes } from "./history-model";

const UNKNOWN = "—";

/** How far a running pass is, and how fast it is going. */
export function WorkingFigures({ file }: { file: ProcessingFile }) {
  const percent = file.progress_percent ?? 0;
  return (
    <div className="mm-history-progress">
      <div
        className="mm-history-progress__bar"
        role="progressbar"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(percent)}
        aria-label="Through the file"
      >
        <i style={{ width: `${percent}%` }} />
      </div>
      <dl className="mm-history-figures">
        <div>
          <dt>Through the file</dt>
          <dd>{Math.round(percent)}%</dd>
        </div>
        {file.progress_speed ? (
          <div>
            <dt>Speed</dt>
            <dd>{file.progress_speed}</dd>
          </div>
        ) : null}
        {file.progress_elapsed_seconds != null ? (
          <div>
            <dt>Running for</dt>
            <dd>{tookWords(file.progress_elapsed_seconds)}</dd>
          </div>
        ) : null}
        {file.progress_eta_seconds != null ? (
          <div>
            <dt>About</dt>
            <dd>{tookWords(file.progress_eta_seconds)} left</dd>
          </div>
        ) : null}
      </dl>
    </div>
  );
}

/** Before, after and saved: always all three, since a gap reads as "Weir does not know". */
export function SizeFigures({ sizes }: { sizes: DetailSizes }) {
  return (
    <div className="mm-history-sizes" data-testid="history-sizes">
      <dl className="mm-history-figures">
        <div>
          <dt>Before</dt>
          <dd>{sizes.before == null ? UNKNOWN : formatBytes(sizes.before)}</dd>
        </div>
        <div>
          <dt>After</dt>
          <dd>
            {sizes.after == null ? "No new copy" : formatBytes(sizes.after)}
          </dd>
        </div>
        <div>
          <dt>Saved</dt>
          <dd className="mm-history-saved">
            {sizes.saved == null
              ? UNKNOWN
              : sizes.saved === 0
                ? "0 B"
                : formatBytes(sizes.saved)}
          </dd>
        </div>
      </dl>
      {sizes.note ? <p className="mm-history-note">{sizes.note}</p> : null}
    </div>
  );
}
