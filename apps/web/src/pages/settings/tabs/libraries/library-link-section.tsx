import { useState } from "react";
import { Link } from "react-router-dom";

import { Field } from "../../../../components/shared/field";
import { QuietFieldGroup } from "../../../../components/shared/quiet-section";
import { WorkflowKindSummary } from "../../../../components/shared/workflow-kind";
import type { MediaManagerConnection } from "../../../../lib/media-managers/media-managers-api";
import { workflowKindOf } from "../../../../lib/processing/workflow-kind";
import {
  workflowStory,
  type WorkflowPath,
} from "../../../../lib/processing/workflow-story";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";

/**
 * Whether a workflow is Weir only or linked to a media manager, and how to change that. Weir only is a
 * complete setup and reads as one, with the link offered as an option. Linking and unlinking change the
 * workflow being edited and save with it.
 */
export function LibraryLinkSection({
  linkedIds,
  path,
  connections,
  editable,
  onLink,
  onUnlink,
}: {
  /** The media managers the workflow is linked to, as it is being edited. */
  linkedIds: number[];
  /** The folders as they are being edited, for the Weir only story. */
  path: WorkflowPath;
  connections: MediaManagerConnection[];
  editable: boolean;
  onLink: (connectionId: number) => void;
  onUnlink: () => void;
}) {
  const kind = workflowKindOf(
    { manager_connection_ids: linkedIds },
    connections,
  );
  const [chosen, setChosen] = useState("");

  return (
    <QuietFieldGroup
      title="Media manager"
      detail="Weir only, or linked to a media manager that hands this workflow its downloads."
    >
      <div className="mm-quiet-stack" data-testid="library-link-section">
        <p>
          <WorkflowKindSummary kind={kind} />
        </p>
        {kind.kind === "weir_only" ? (
          <p className="mm-quiet-note" data-testid="workflow-story">
            {workflowStory(path, null, null)}
          </p>
        ) : null}
        {kind.kind === "weir_only" ? (
          <WeirOnlyOptions
            connections={connections}
            editable={editable}
            chosen={chosen}
            onChoose={setChosen}
            onLink={() => {
              onLink(Number(chosen));
              setChosen("");
            }}
          />
        ) : (
          <div className="space-y-2">
            <p className="mm-quiet-note">
              Unlinking makes this workflow Weir only. Weir stops waiting for{" "}
              {kind.managers.map((m) => m.name).join(" and ")} to finish with a
              download, stops handing cleaned files back to it, and stops
              telling it what happened. The folders, the rules and the files
              already cleaned stay as they are. It takes effect when you save.
            </p>
            <button
              type="button"
              data-testid="library-unlink"
              className={mmActionButtonClass({ variant: "secondary" })}
              disabled={!editable}
              onClick={onUnlink}
            >
              Unlink
            </button>
          </div>
        )}
      </div>
    </QuietFieldGroup>
  );
}

function WeirOnlyOptions({
  connections,
  editable,
  chosen,
  onChoose,
  onLink,
}: {
  connections: MediaManagerConnection[];
  editable: boolean;
  chosen: string;
  onChoose: (connectionId: string) => void;
  onLink: () => void;
}) {
  if (connections.length === 0) {
    return (
      <p className="mm-quiet-note">
        Link a media manager if one hands this workflow its downloads.{" "}
        <Link className="mm-quiet-link" to="/settings?tab=media-managers">
          Connect Deluno, Sonarr or Radarr
        </Link>{" "}
        first.
      </p>
    );
  }
  return (
    <>
      <p className="mm-quiet-note">
        Link a media manager if one hands this workflow its downloads. Weir then
        waits for it to finish with a download, hands the cleaned file back, and
        can ask it for a different release when one is bad.
      </p>
      <div className="mm-field-row">
        <Field label="Media manager" width="medium">
          <select
            data-testid="library-manager-choice"
            className="mm-input"
            value={chosen}
            disabled={!editable}
            onChange={(e) => onChoose(e.target.value)}
          >
            <option value="">Choose a media manager</option>
            {connections.map((connection) => (
              <option key={connection.id} value={connection.id}>
                {connection.name}
              </option>
            ))}
          </select>
        </Field>
      </div>
      <div>
        <button
          type="button"
          data-testid="library-link"
          className={mmActionButtonClass({ variant: "secondary" })}
          disabled={!editable || chosen === ""}
          onClick={onLink}
        >
          Link to a media manager
        </button>
      </div>
    </>
  );
}
