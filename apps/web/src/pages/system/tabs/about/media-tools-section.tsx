import { Link } from "react-router-dom";

import { Panel } from "../../../../components/panels/panel";
import { useProcessingRuntimeSettingsQuery } from "../../../../lib/processing/maintenance-queries";
import {
  toolVersion,
  useMediaToolsQuery,
} from "../../../../lib/system/media-tools";

const NOT_INSTALLED = "not installed";
const SPEED_SETTINGS_PATH = "/setup/performance";

/**
 * System › About: the tools Weir reads and writes media with, one short line each. What it never does to a
 * file is here too, so nobody has to ask.
 */
export function MediaToolsSection() {
  const tools = useMediaToolsQuery();
  const runtime = useProcessingRuntimeSettingsQuery();
  const ffmpeg = tools.data?.ffmpeg;
  const mkvmerge = tools.data?.mkvmerge;
  const ffmpegMissing = ffmpeg === NOT_INSTALLED;
  const mkvmergeMissing = mkvmerge === NOT_INSTALLED;
  const hasMkvmerge = Boolean(mkvmerge) && !mkvmergeMissing;

  return (
    <Panel
      title="Media tools"
      headingId="about-media-tools-heading"
      padded
      dataTestId="about-facts"
    >
      <dl className="mm-kv mm-sys-facts">
        <div>
          <dt>FFmpeg</dt>
          <dd
            className={ffmpegMissing ? "mm-status-text--failed" : undefined}
            title={
              ffmpegMissing ? "Nothing can be processed without it." : ffmpeg
            }
          >
            {tools.isLoading
              ? "Checking…"
              : ffmpegMissing
                ? "Not found"
                : toolVersion(ffmpeg)}
          </dd>
        </div>
        <div>
          <dt>mkvmerge</dt>
          <dd title={mkvmerge}>
            {tools.isLoading
              ? "Checking…"
              : mkvmergeMissing
                ? "Not installed (optional)"
                : toolVersion(mkvmerge)}
          </dd>
        </div>
        <div>
          <dt>Reads with</dt>
          <dd title="FFprobe comes with FFmpeg.">FFprobe</dd>
        </div>
        <div>
          <dt>Writes with</dt>
          <dd
            title={
              hasMkvmerge
                ? "A workflow can be set to FFmpeg only in Setup › Workflows."
                : "With mkvmerge installed, Weir writes MKV files with it."
            }
          >
            {hasMkvmerge ? "mkvmerge (MKV), FFmpeg (others)" : "FFmpeg"}
          </dd>
        </div>
        <div>
          <dt>Re-encoding</dt>
          <dd>Never: tracks are copied as they are</dd>
        </div>
        {runtime.data ? (
          <>
            <div>
              <dt>Worker slots</dt>
              <dd>
                {runtime.data.in_process_processing_worker_count} (
                <Link to={SPEED_SETTINGS_PATH} className="mm-sys-link">
                  Setup › Performance › Speed
                </Link>
                )
              </dd>
            </div>
            <div>
              <dt>File types</dt>
              <dd>{runtime.data.processing_media_extensions.join(" ")}</dd>
            </div>
          </>
        ) : null}
      </dl>
    </Panel>
  );
}
