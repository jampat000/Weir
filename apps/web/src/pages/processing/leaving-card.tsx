import { FileName } from "../../components/shared/file-name";
import type { ProcessingFile } from "../../lib/processing/files-api";
import { SourceTag } from "./lane-cards";
import type { LeavingCard as Leaving } from "./leaving-cards";
import { StageFlow } from "./stage-flow";
import { FLOW_DONE } from "./stage-flow-model";

/** One line under the flow for an end that has no reason of its own to give. */
const DONE_NOTE = "Done. It moves to Just finished.";

/**
 * A card for the moment after its file has left Working or Handing back: the same names, with the flow drawn
 * at its end. A finished file has every step ticked; a failed or rejected one marks the step it stopped at
 * and says why. It stays only a few seconds, while the file appears in Just finished or Needs you.
 */
export function LeavingCard({
  card,
  onOpen,
}: {
  card: Leaving;
  onOpen: (file: ProcessingFile) => void;
}) {
  const { outcome } = card;
  return (
    <li
      className="mm-live-card mm-live-card--leaving"
      data-testid="live-leaving"
      data-outcome={outcome.kind}
    >
      <SourceTag source={card.source} libraryName={card.libraryName} />
      <span className="mm-live-card__title">
        <button
          type="button"
          className="mm-live-card__open"
          onClick={() => onOpen(card.file)}
        >
          {card.name}
        </button>
      </span>
      <FileName path={card.path} className="mm-live-card__file" />
      {outcome.kind === "done" ? (
        <>
          <StageFlow position={FLOW_DONE} />
          <p className="mm-live-card__note" role="status">
            {DONE_NOTE}
          </p>
        </>
      ) : (
        <StageFlow
          position={outcome.at}
          failure={{ at: outcome.at, reason: outcome.reason }}
        />
      )}
    </li>
  );
}
