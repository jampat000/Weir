import type { ProcessingFileTrack } from "../../lib/processing/files-api";
import { plural } from "../../lib/ui/mm-plural";
import { trackLabel, type TrackChoice } from "./use-track-choice";

function TrackRow({
  track,
  choice,
}: {
  track: ProcessingFileTrack;
  choice: TrackChoice;
}) {
  const state = choice.rows[track.index];
  const kept = state?.keep ?? false;
  const label = trackLabel(track);
  const type = track.type;
  return (
    <tr data-testid={`choose-tracks-row-${track.index}`}>
      <td>
        <input
          type="checkbox"
          checked={kept}
          onChange={(e) => choice.setKeep(track, e.target.checked)}
          aria-label={`Keep ${label}`}
          data-testid={`choose-tracks-keep-${track.index}`}
        />
      </td>
      <td className="mm-tracks-table__track">{label}</td>
      <td>
        {type === "audio" || type === "subtitle" ? (
          <input
            type="radio"
            name={`choose-tracks-default-${type}`}
            checked={state?.default ?? false}
            disabled={!kept}
            onChange={() => choice.setDefault(type, track.index)}
            aria-label={`Make ${label} the default ${type} track`}
            data-testid={`choose-tracks-default-${track.index}`}
          />
        ) : null}
      </td>
      <td>
        {track.type === "subtitle" ? (
          <input
            type="checkbox"
            checked={state?.forced ?? false}
            disabled={!kept}
            onChange={(e) => choice.setForced(track.index, e.target.checked)}
            aria-label={`Mark ${label} forced`}
            data-testid={`choose-tracks-forced-${track.index}`}
          />
        ) : null}
      </td>
      <td className="mm-tracks-table__rule">
        <strong>{track.rule_would_keep ? "Would keep" : "Would remove"}</strong>{" "}
        — {track.rule_reason}
      </td>
    </tr>
  );
}

/** Every video, audio and subtitle track with its keep, default and forced choices. */
export function TrackTable({ choice }: { choice: TrackChoice }) {
  return (
    <div className="mm-tracks-table-wrap">
      <table className="mm-tracks-table">
        <thead>
          <tr>
            <th scope="col">Keep</th>
            <th scope="col">Track</th>
            <th scope="col">Default</th>
            <th scope="col">Forced</th>
            <th scope="col">What the saved rules would do</th>
          </tr>
        </thead>
        <tbody>
          {choice.selectable.map((track) => (
            <TrackRow key={track.index} track={track} choice={choice} />
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** Images and attachments follow the saved rules, so they are listed but not offered. */
export function OtherStreams({ tracks }: { tracks: ProcessingFileTrack[] }) {
  if (tracks.length === 0) return null;
  return (
    <details className="mm-story-pass__detail">
      <summary>
        {plural(
          tracks.length,
          "embedded image or attachment stream",
          "embedded image or attachment streams",
        )}{" "}
        not shown above — these are handled by the saved rules, not by this
        choice
      </summary>
      <ul>
        {tracks.map((track) => (
          <li key={track.index}>
            #{track.index} {track.type} —{" "}
            {track.rule_would_keep ? "kept" : "removed"}: {track.rule_reason}
          </li>
        ))}
      </ul>
    </details>
  );
}

/**
 * The kept tracks in output order. Plain up and down buttons rather than drag and drop, so it works
 * the same with a keyboard, a screen reader or a mouse.
 */
export function TrackOrder({ choice }: { choice: TrackChoice }) {
  const kept = choice.keptTracks;
  return (
    <section className="mm-tracks-order" aria-label="Track order">
      <h3 className="mm-story-pass__when">Order</h3>
      {kept.length === 0 ? (
        <p className="mm-story-panel__note">Nothing is kept yet.</p>
      ) : (
        <ol className="mm-tracks-order__list">
          {kept.map((track, position) => (
            <li
              key={track.index}
              data-testid={`choose-tracks-order-${track.index}`}
            >
              <span>{trackLabel(track)}</span>
              <span className="mm-tracks-order__buttons">
                <button
                  type="button"
                  onClick={() => choice.move(track.index, -1)}
                  disabled={position === 0}
                  aria-label={`Move ${trackLabel(track)} earlier`}
                  data-testid={`choose-tracks-move-up-${track.index}`}
                >
                  ↑
                </button>
                <button
                  type="button"
                  onClick={() => choice.move(track.index, 1)}
                  disabled={position === kept.length - 1}
                  aria-label={`Move ${trackLabel(track)} later`}
                  data-testid={`choose-tracks-move-down-${track.index}`}
                >
                  ↓
                </button>
              </span>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}

export function DroppedTracks({ tracks }: { tracks: ProcessingFileTrack[] }) {
  return (
    <section
      className="mm-tracks-dropped"
      aria-label="Tracks that will be dropped"
    >
      <h3 className="mm-story-pass__when">Will be dropped</h3>
      {tracks.length === 0 ? (
        <p className="mm-story-panel__note">
          Nothing — every video, audio and subtitle track is kept.
        </p>
      ) : (
        <ul
          className="mm-tracks-order__list"
          data-testid="choose-tracks-dropped-list"
        >
          {tracks.map((track) => (
            <li key={track.index}>{trackLabel(track)}</li>
          ))}
        </ul>
      )}
    </section>
  );
}
