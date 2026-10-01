import { useMutation } from "@tanstack/react-query";
import { useState } from "react";
import { Link } from "react-router-dom";

import { Field } from "../../../../components/shared/field";
import {
  QuietSection,
  quietActionRowClass,
} from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import type { MediaManagerConnection } from "../../../../lib/media-managers/media-managers-api";
import {
  PROCESSING_MEDIA_TYPE_LABELS,
  type ProcessingLibrary,
} from "../../../../lib/processing/libraries-api";
import {
  fetchLibrarySuggestions,
  type SuggestedLibrary,
} from "../../../../lib/processing/library-setup-api";
import { connectionTitle } from "../../../../lib/ui/connection-title";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import type { LibraryForm } from "./library-form";

/** What the editor opens with: a blank workflow or the one filled in, and the workflow it fills in when it is one. */
export type WorkflowStart = {
  preset: Partial<LibraryForm>;
  /** An existing workflow with no folders yet, which the suggestion fills in rather than adding another. */
  fills: ProcessingLibrary | null;
};

type Choice = "local" | "manager";

function presetFrom(
  suggestion: SuggestedLibrary,
  connectionId: number,
): Partial<LibraryForm> {
  return {
    name: suggestion.name,
    media_type: suggestion.media_type,
    watched_folder: suggestion.watched_folder,
    output_folder: suggestion.output_folder,
    manager_connection_id: String(connectionId),
  };
}

/**
 * Adding a workflow starts by asking which kind: Weir only (local folders, today's form) or from a media
 * manager, which fills the folders in from what that manager reports. Nothing is saved here: both end in the
 * editor, which saves on its own Save through the ordinary library rules.
 */
export function AddWorkflowChoice({
  connections,
  workflows,
  initialConnectionId,
  onStart,
  onCancel,
}: {
  connections: MediaManagerConnection[];
  /** A media manager to start from, when the person came from it. */
  initialConnectionId?: number;
  workflows: ProcessingLibrary[];
  onStart: (start: WorkflowStart) => void;
  onCancel: () => void;
}) {
  const [choice, setChoice] = useState<Choice>(
    initialConnectionId === undefined ? "local" : "manager",
  );
  const [connectionId, setConnectionId] = useState(
    initialConnectionId === undefined
      ? connections[0]
        ? String(connections[0].id)
        : ""
      : String(initialConnectionId),
  );
  const [offered, setOffered] = useState<SuggestedLibrary[] | null>(null);
  const [pickedType, setPickedType] = useState<string>("");
  const suggestions = useMutation({ mutationFn: fetchLibrarySuggestions });
  const hasManagers = connections.length > 0;

  const start = (suggestion: SuggestedLibrary) => {
    const fills =
      suggestion.library_id === null
        ? null
        : (workflows.find((w) => w.id === suggestion.library_id) ?? null);
    onStart({
      preset: presetFrom(suggestion, Number(connectionId)),
      fills,
    });
  };

  const ask = () =>
    suggestions.mutate(undefined, {
      onSuccess: (result) => {
        const forThisManager = result.libraries.filter((library) =>
          library.manager_connection_ids.includes(Number(connectionId)),
        );
        if (forThisManager.length === 1) {
          start(forThisManager[0]);
        } else if (forThisManager.length === 0) {
          onStart({
            preset: { manager_connection_id: connectionId },
            fills: null,
          });
        } else {
          setOffered(forThisManager);
          setPickedType(forThisManager[0].media_type);
        }
      },
    });

  const proceed = () => {
    if (choice === "local") {
      onStart({ preset: {}, fills: null });
      return;
    }
    const picked = offered?.find((s) => s.media_type === pickedType);
    if (picked) start(picked);
    else ask();
  };

  return (
    <QuietSection
      headingId="add-workflow-heading"
      heading="Add a workflow"
      level={3}
    >
      <div className="mm-quiet-stack" data-testid="add-workflow-choice">
        <fieldset className="mm-wizard-choices">
          <legend className="mm-wizard-label">Which kind?</legend>
          <label className="mm-wizard-choice">
            <input
              type="radio"
              name="add-workflow-kind"
              className="mt-0.5 h-4 w-4 shrink-0 accent-mm-accent"
              checked={choice === "local"}
              onChange={() => setChoice("local")}
            />
            <span>
              <span className="font-medium">Local folders</span>
              <span className="block text-sm text-mm-text2">
                Weir only: Weir watches a folder you choose and writes cleaned
                files to another. Nothing else is involved.
              </span>
            </span>
          </label>
          <label className="mm-wizard-choice">
            <input
              type="radio"
              name="add-workflow-kind"
              className="mt-0.5 h-4 w-4 shrink-0 accent-mm-accent"
              checked={choice === "manager"}
              disabled={!hasManagers}
              onChange={() => setChoice("manager")}
            />
            <span>
              <span className="font-medium">From a media manager</span>
              <span className="block text-sm text-mm-text2">
                {hasManagers ? (
                  "The folders are filled in from what the media manager reports, and the workflow is linked to it."
                ) : (
                  <>
                    Nothing is connected yet.{" "}
                    <Link
                      className="mm-quiet-link"
                      to="/settings?tab=media-managers"
                    >
                      Connect Deluno, Sonarr or Radarr
                    </Link>{" "}
                    first.
                  </>
                )}
              </span>
            </span>
          </label>
        </fieldset>

        {choice === "manager" && hasManagers ? (
          <Field label="Which media manager?" width="medium">
            <select
              className="mm-input"
              value={connectionId}
              onChange={(e) => {
                setConnectionId(e.target.value);
                setOffered(null);
              }}
            >
              {connections.map((connection) => (
                <option key={connection.id} value={connection.id}>
                  {connectionTitle(connection)}
                </option>
              ))}
            </select>
          </Field>
        ) : null}

        {choice === "manager" && offered ? (
          <Field label="Which one?" width="medium">
            <select
              className="mm-input"
              value={pickedType}
              onChange={(e) => setPickedType(e.target.value)}
            >
              {offered.map((library) => (
                <option key={library.media_type} value={library.media_type}>
                  {PROCESSING_MEDIA_TYPE_LABELS[library.media_type]}
                </option>
              ))}
            </select>
          </Field>
        ) : null}

        {suggestions.isError ? (
          <p className="mm-status-text--failed text-sm" role="alert">
            {errorMessage(
              suggestions.error,
              "Weir could not ask that media manager for its folders. Choose Local folders, or try again.",
            )}
          </p>
        ) : null}

        <div className={quietActionRowClass}>
          <button
            type="button"
            data-testid="add-workflow-continue"
            className={mmActionButtonClass({ variant: "primary" })}
            disabled={suggestions.isPending}
            onClick={proceed}
          >
            {suggestions.isPending ? "Asking…" : "Continue"}
          </button>
          <button
            type="button"
            className={mmActionButtonClass({ variant: "tertiary" })}
            onClick={onCancel}
          >
            Cancel
          </button>
        </div>
      </div>
    </QuietSection>
  );
}
