/**
 * One library file, opened from the table: what is in it, what your rules would take out of it and why, and
 * the things you can do about it. The track list is the same "try on a file" preview the rule-set editor
 * uses (#502), so this screen and that one can never disagree about a file.
 *
 * Two of those things are yours rather than the rules': you can pick the tracks for this one file, the way a
 * held download already lets you (#501), and you can set the file aside so nothing cleans it at all.
 *
 * A slide-over, like the file story on Live: close it and the table is exactly where it was.
 */
import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { SidePanel } from "../../components/shared/side-panel";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import { useActivityRecentQuery } from "../../lib/activity/queries";
import { formatBytes } from "../../lib/format/bytes";
import type {
  LibraryFile,
  LibraryManualPlan,
} from "../../lib/processing/library-api";
import {
  previewProcessingRules,
  type ProcessingRulesPreviewTrack,
} from "../../lib/processing/rules-preview-api";
import { baseName } from "../../lib/format/path";
import { errorMessage } from "../../lib/api/error-message";

function when(iso: string): string {
  const at = new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(iso) ? iso : `${iso}Z`);
  return Number.isNaN(at.getTime()) ? iso : at.toLocaleString();
}

function trackLabel(track: ProcessingRulesPreviewTrack): string {
  return [
    track.language || "Undetermined",
    track.codec.toUpperCase(),
    track.channels ? `${track.channels}ch` : "",
    track.title,
  ]
    .filter(Boolean)
    .join(" · ");
}

export function LibraryFileDrawer({
  libraryId,
  libraryName,
  file,
  onClose,
  onClean,
  onLeaveAlone,
  cleaning,
  settingAside,
}: {
  libraryId: number;
  libraryName: string;
  file: LibraryFile | null;
  onClose: () => void;
  /** Without a plan the library's rules decide; with one, exactly these tracks are kept. */
  onClean: (path: string, manual?: LibraryManualPlan) => void;
  onLeaveAlone: (path: string, leaveAlone: boolean) => void;
  cleaning: boolean;
  settingAside: boolean;
}): React.ReactElement | null {
  const open = file !== null;
  // The tracks you have said to keep, once you start choosing; null while the rules are deciding.
  const [keep, setKeep] = useState<Set<number> | null>(null);

  const preview = useQuery({
    queryKey: [
      "processing",
      "library-file-preview",
      libraryId,
      file?.path ?? "",
    ],
    queryFn: () =>
      previewProcessingRules({ libraryId, absolutePath: file!.path }),
    // A preview runs a real read of a real file, so it is asked for once per opened file, not on a timer.
    enabled: open,
    staleTime: 5 * 60 * 1000,
    retry: false,
  });
  const history = useActivityRecentQuery(
    open ? { limit: 6, file: file.path } : undefined,
  );

  // Opening another file starts again: a choice is about the file you were looking at, and nothing else.
  useEffect(() => setKeep(null), [file?.path]);

  if (!file) return null;

  const name = baseName(file.path);
  const tracks = preview.data?.tracks ?? [];
  const audio = tracks.filter((track) => track.type === "audio");
  const subtitles = tracks.filter((track) => track.type === "subtitle");
  const choosing = keep !== null;
  const kept = keep ?? new Set(tracks.filter(rulesKeep).map((t) => t.index));
  const keptAudio = audio.filter((track) => kept.has(track.index)).length;
  const removing = tracks.filter(
    (track) => track.type !== "video" && !kept.has(track.index),
  ).length;

  // A file set aside is never cleaned; a choice must keep some audio and must actually change something; and
  // without a choice, only a file the rules would change has anything to do.
  const cannotClean =
    cleaning ||
    file.leave_alone ||
    (choosing
      ? keptAudio === 0 || removing === 0
      : file.classification !== "would_change");

  const manualPlan = (): LibraryManualPlan => {
    const chosen = tracks.filter(
      (track) => track.type === "video" || kept.has(track.index),
    );
    const firstAudio = chosen.find((track) => track.type === "audio");
    return {
      keep: chosen.map((track) => ({
        index: track.index,
        default: track.index === firstAudio?.index,
        forced: track.forced,
      })),
      order: chosen.map((track) => track.index),
      expected_size_bytes: file.size_bytes,
    };
  };

  return (
    <SidePanel
      open={open}
      title={name}
      eyebrow={`Library · ${libraryName}${file.manager_title ? ` · ${file.manager_title}` : ""}`}
      subtitle={file.path}
      onClose={onClose}
      dataTestId="library-file-drawer"
    >
      <>
        <div className="mm-drawer__chips">
          {file.video_height ? (
            <span className="mm-drawer__chip">{file.video_height}p</span>
          ) : null}
          {file.video_codec && file.video_codec !== "unknown" ? (
            <span className="mm-drawer__chip">
              {file.video_codec.toUpperCase()}
            </span>
          ) : null}
          <span className="mm-drawer__chip">
            {formatBytes(file.size_bytes)}
          </span>
          {file.manager_kind ? (
            <span className="mm-drawer__chip mm-drawer__chip--ok">
              {file.manager_kind === "sonarr" ? "Sonarr" : "Radarr"}: matched
            </span>
          ) : null}
          {file.cleaned_at ? (
            <span className="mm-drawer__chip mm-drawer__chip--ok">
              Cleaned {new Date(file.cleaned_at * 1000).toLocaleDateString()}
            </span>
          ) : null}
          {file.leave_alone ? (
            <span className="mm-drawer__chip">Set aside</span>
          ) : null}
        </div>

        <p className="mm-drawer__verdict">
          {choosing
            ? keptAudio === 0
              ? "Keep at least one audio track."
              : removing === 0
                ? "That is everything this file already holds, so there would be nothing to do."
                : `Keeping ${kept.size} of ${tracks.length} tracks; ${removing} would come out for good.`
            : (file.summary ??
              file.reason ??
              "Weir has not judged this file yet.")}
        </p>

        {preview.isPending ? (
          <p className="mm-drawer__note">{"Reading this file’s tracks…"}</p>
        ) : preview.isError ? (
          <p className="mm-drawer__note">
            Weir could not read this file just now:{" "}
            {errorMessage(preview.error, "it could not be opened.")}
          </p>
        ) : (
          <>
            <TrackList
              title={`Audio · ${audio.length} ${audio.length === 1 ? "track" : "tracks"}`}
              tracks={audio}
              kept={choosing ? kept : null}
              onToggle={(index) => setKeep(toggled(kept, index))}
            />
            <TrackList
              title={`Subtitles · ${subtitles.length} ${subtitles.length === 1 ? "track" : "tracks"}`}
              tracks={subtitles}
              kept={choosing ? kept : null}
              onToggle={(index) => setKeep(toggled(kept, index))}
            />
            <p className="mm-drawer__note">
              {choosing ? (
                <>
                  {"Your choice, for this file only. "}
                  <button
                    type="button"
                    className="mm-link-button"
                    onClick={() => setKeep(null)}
                  >
                    Use this library’s rules instead
                  </button>
                </>
              ) : (
                <>
                  {"Decided by this library’s rules. "}
                  <button
                    type="button"
                    className="mm-link-button"
                    disabled={tracks.length === 0}
                    onClick={() => setKeep(new Set(kept))}
                  >
                    Choose the tracks yourself
                  </button>
                  {" · "}
                  <Link to={`/settings?tab=rules&library=${libraryId}`}>
                    Edit the rules →
                  </Link>
                </>
              )}
            </p>
          </>
        )}

        <div className="mm-drawer__actions">
          <button
            type="button"
            className={mmActionButtonClass({ variant: "primary" })}
            disabled={cannotClean}
            onClick={() =>
              onClean(file.path, choosing ? manualPlan() : undefined)
            }
          >
            {choosing ? "Clean with these tracks" : "Clean this file"}
          </button>
          <button
            type="button"
            className="mm-head-control"
            disabled={preview.isFetching}
            onClick={() => void preview.refetch()}
          >
            Check it again
          </button>
        </div>

        <label className="mm-drawer__aside">
          <input
            type="checkbox"
            checked={file.leave_alone}
            disabled={settingAside}
            onChange={(event) => onLeaveAlone(file.path, event.target.checked)}
          />
          <span>
            Leave this file alone. Nothing cleans it — not a scan, not the
            schedule, not picking it in the table — until you clear this.
          </span>
        </label>

        <p className="mm-drawer__note">
          Cleaning rewrites this file where it sits and tells your media manager
          to look at it again. A removed track only comes back by downloading
          the title again, unless keeping originals is on for this library.
        </p>

        {(history.data?.items ?? []).length > 0 ? (
          <div className="mm-drawer__history">
            <p className="mm-drawer__eyebrow">What has happened to this file</p>
            {(history.data?.items ?? []).map((item) => (
              <p key={item.id} className="mm-drawer__step">
                <span className="mm-drawer__when">{when(item.created_at)}</span>
                {item.title}
              </p>
            ))}
          </div>
        ) : null}
      </>
    </SidePanel>
  );
}

const rulesKeep = (track: ProcessingRulesPreviewTrack): boolean =>
  track.action === "keep";

function toggled(kept: Set<number>, index: number): Set<number> {
  const next = new Set(kept);
  if (next.has(index)) next.delete(index);
  else next.add(index);
  return next;
}

function TrackList({
  title,
  tracks,
  kept,
  onToggle,
}: {
  title: string;
  tracks: ProcessingRulesPreviewTrack[];
  /** The tracks you have chosen to keep, or null while the rules are deciding. */
  kept: Set<number> | null;
  onToggle: (index: number) => void;
}): React.ReactElement | null {
  if (tracks.length === 0) return null;
  return (
    <div className="mm-drawer__tracks">
      <p className="mm-drawer__eyebrow">{title}</p>
      {tracks.map((track) => {
        const keeping = kept ? kept.has(track.index) : rulesKeep(track);
        const row = (
          <>
            <span
              className={`mm-drawer__verdict-tag mm-drawer__verdict-tag--${keeping ? "keep" : "drop"}`}
            >
              {keeping ? "Keep" : "Remove"}
            </span>
            <span className="mm-drawer__track-name">{trackLabel(track)}</span>
            <span className="mm-drawer__track-why">
              {kept ? "" : (track.reasons[0] ?? "")}
            </span>
          </>
        );
        return kept ? (
          <label
            key={track.index}
            className="mm-drawer__track mm-drawer__track--choosable"
          >
            <input
              type="checkbox"
              checked={keeping}
              onChange={() => onToggle(track.index)}
            />
            {row}
          </label>
        ) : (
          <div key={track.index} className="mm-drawer__track">
            {row}
          </div>
        );
      })}
    </div>
  );
}
