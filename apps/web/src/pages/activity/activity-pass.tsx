import { Chip } from "../../components/panels/chip";
import type { ProcessingFileLogEntry } from "../../lib/processing/files-api";
import {
  agoWords,
  tracksFromRecord,
  type ActivityTrack,
} from "./activity-model";

/**
 * What Weir kept and removed is the point of the page, so it reads first and plainly: a heading with
 * the two counts, each track in full text with a Kept or Removed label, and why a track went under
 * its name.
 */
function TrackSet({ tracks }: { tracks: ActivityTrack[] }) {
  const keptCount = tracks.filter((t) => t.kept).length;
  const removedCount = tracks.length - keptCount;
  return (
    <section
      className="mm-history-trackset"
      aria-labelledby="activity-tracks-heading"
      data-testid="activity-tracks"
    >
      <h3 id="activity-tracks-heading" className="mm-history-trackset__head">
        Tracks
        <Chip tone="healthy">{keptCount} kept</Chip>
        <Chip tone={removedCount > 0 ? "failed" : "neutral"}>
          {removedCount} removed
        </Chip>
      </h3>
      <table className="mm-history-tracks">
        <tbody>
          {tracks.map((track, index) => (
            <tr
              key={`${track.kind}-${index}`}
              className={track.kept ? "is-kept" : "is-removed"}
            >
              <th scope="row">{track.kind}</th>
              <td className="mm-history-track">
                <span className="mm-history-track__what">{track.what}</span>
                {!track.kept && track.why ? (
                  <span className="mm-history-track__why">{track.why}</span>
                ) : null}
              </td>
              <td className="mm-history-track__verdict">
                <Chip tone={track.kept ? "healthy" : "failed"}>
                  {track.kept ? "Kept" : "Removed"}
                </Chip>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}

/** The latest pass over a file: its tracks, the story of what happened, and when it was recorded. */
export function PassRecord({
  pass,
  now,
}: {
  pass: ProcessingFileLogEntry;
  now: number;
}) {
  const tracks = tracksFromRecord(pass.detail);
  return (
    <>
      {tracks.length > 0 ? <TrackSet tracks={tracks} /> : null}
      {pass.story.length > 0 ? (
        <ol className="mm-history-story" aria-label="What happened">
          {pass.story.map((step, index) => (
            <li key={index} className={`is-${step.tone}`}>
              <b>{step.heading}</b>
              <span>{step.sentence}</span>
            </li>
          ))}
        </ol>
      ) : null}
      <p className="mm-history-record-meta">
        Recorded {agoWords(pass.recorded_at, now)}
      </p>
    </>
  );
}
