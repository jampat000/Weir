import { useProcessingRuntimeSettingsQuery } from "../../../../lib/processing/maintenance-queries";
import {
  toolVersion,
  useMediaToolsQuery,
} from "../../../../lib/system/media-tools";

/**
 * System › About's first column (was This instance; canvas board 11, 23 Sep 2026): what this Weir is and what it works
 * with, as facts, one per line. It replaced three paragraphs about worker modes and SQLite that James found hard to
 * read; those facts come from the environment, so nothing here is editable.
 */
export function AboutFacts() {
  const runtime = useProcessingRuntimeSettingsQuery();
  const tools = useMediaToolsQuery();
  const ffmpeg = tools.data?.ffmpeg;
  const mkvmerge = tools.data?.mkvmerge;
  const ffmpegMissing = ffmpeg === "not installed";

  return (
    <section
      className="mm-quiet-section"
      aria-labelledby="about-facts-heading"
      data-testid="about-facts"
    >
      <div className="mm-quiet-section__head">
        <h2 id="about-facts-heading" className="mm-quiet-section__title">
          What Weir works with
        </h2>
      </div>
      <dl className="mm-kv">
        <div>
          <dt>FFmpeg</dt>
          <dd
            className={ffmpegMissing ? "mm-status-text--failed" : undefined}
            title={ffmpeg}
          >
            {tools.isLoading
              ? "Checking…"
              : ffmpegMissing
                ? "Not found, so nothing can be processed"
                : toolVersion(ffmpeg)}
          </dd>
        </div>
        <div>
          <dt>mkvmerge</dt>
          <dd title={mkvmerge}>
            {tools.isLoading
              ? "Checking…"
              : mkvmerge === "not installed"
                ? "Not installed (optional)"
                : toolVersion(mkvmerge)}
          </dd>
        </div>
        <div>
          <dt>Reads files with</dt>
          <dd>FFprobe, which comes with FFmpeg.</dd>
        </div>
        <div>
          <dt>Writes files with</dt>
          <dd>
            {mkvmerge && mkvmerge !== "not installed"
              ? "mkvmerge for MKV files, FFmpeg for the rest."
              : "FFmpeg. With mkvmerge installed, Weir would write MKV files with it."}{" "}
            A library can be set to FFmpeg only in Settings › Libraries.
          </dd>
        </div>
        <div>
          <dt>Re-encoding</dt>
          <dd>Never. Weir copies the tracks it keeps as they are.</dd>
        </div>
        {runtime.data ? (
          <>
            <div>
              <dt>Worker slots</dt>
              <dd>
                {runtime.data.in_process_processing_worker_count}. Files at
                once, in Settings › Performance, decides how many run.
              </dd>
            </div>
            <div>
              <dt>File types it takes</dt>
              <dd>{runtime.data.processing_media_extensions.join(", ")}</dd>
            </div>
          </>
        ) : null}
      </dl>
    </section>
  );
}
