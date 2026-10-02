import { Panel } from "../../../../components/panels/panel";
import { useProcessingRuntimeSettingsQuery } from "../../../../lib/processing/maintenance-queries";
import { REPOSITORY_URL, LICENSE_URL } from "../../../../lib/repository";
import {
  toolVersion,
  useMediaToolsQuery,
} from "../../../../lib/system/media-tools";
import { useSystemReadinessQuery } from "../../../../lib/system/readiness-queries";

/**
 * System › About's first column: what this Weir is and what it works with, as facts, one per line. They come
 * from the environment, so nothing here is editable.
 */
export function AboutFacts() {
  const runtime = useProcessingRuntimeSettingsQuery();
  const tools = useMediaToolsQuery();
  const machine = useSystemReadinessQuery().data;
  const ffmpeg = tools.data?.ffmpeg;
  const mkvmerge = tools.data?.mkvmerge;
  const ffmpegMissing = ffmpeg === "not installed";

  return (
    <Panel
      title="What Weir works with"
      headingId="about-facts-heading"
      padded
      dataTestId="about-facts"
    >
      <dl className="mm-kv">
        {machine ? (
          <div data-testid="about-machine-name">
            <dt>Name</dt>
            <dd>
              Weir on {machine.machine_name}. Weir takes its name from the
              computer it runs on.
              {machine.machine_name_looks_generated ? (
                <span
                  className="mm-status-text--warning block"
                  data-testid="about-hostname-tip"
                >
                  Set <code>hostname:</code> in your compose file so Weir shows
                  your server&apos;s name.
                </span>
              ) : null}
            </dd>
          </div>
        ) : null}
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
            A workflow can be set to FFmpeg only in Setup › Workflows.
          </dd>
        </div>
        <div>
          <dt>Re-encoding</dt>
          <dd>Never. Weir copies the tracks it keeps as they are.</dd>
        </div>
        <div>
          <dt>Licence</dt>
          <dd>
            <a
              href={LICENSE_URL}
              target="_blank"
              rel="noreferrer"
              className="mm-quiet-link"
            >
              AGPL-3.0-or-later →
            </a>
          </dd>
        </div>
        <div>
          <dt>Source code</dt>
          <dd>
            <a
              href={REPOSITORY_URL}
              target="_blank"
              rel="noreferrer"
              className="mm-quiet-link"
              data-testid="about-source-code-link"
            >
              View on GitHub →
            </a>
          </dd>
        </div>
        {runtime.data ? (
          <>
            <div>
              <dt>Worker slots</dt>
              <dd>
                {runtime.data.in_process_processing_worker_count}. Files at
                once, in Setup › Performance › Speed, decides how many run.
              </dd>
            </div>
            <div>
              <dt>File types it takes</dt>
              <dd>{runtime.data.processing_media_extensions.join(", ")}</dd>
            </div>
          </>
        ) : null}
      </dl>
    </Panel>
  );
}
