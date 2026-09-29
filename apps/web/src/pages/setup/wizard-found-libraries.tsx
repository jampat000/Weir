import { SetupCheckLines } from "../../components/shared/setup-check-lines";
import { WorkflowKindBadge } from "../../components/shared/workflow-kind";
import type { MediaManagerConnection } from "../../lib/media-managers/media-managers-api";
import { workflowKindOf } from "../../lib/processing/workflow-kind";
import type { ProposedLibraryCheck } from "../../lib/processing/library-setup-api";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import type {
  useSuggestedLibraries,
  SuggestedRow,
} from "./use-suggested-libraries";
import { FolderInput } from "./setup-wizard-parts";

type Found = ReturnType<typeof useSuggestedLibraries>;

/** Every finding that needs a fix or could not be verified, from Weir's own folders, the managers and the download clients. */
function attentionLines(check: ProposedLibraryCheck) {
  const chain = check.chain;
  if (!chain) return [];
  return [
    ...chain.local.lines,
    ...chain.managers.flatMap((manager) => manager.lines),
    ...chain.download_clients.flatMap((client) => client.lines),
  ].filter((line) => line.state === "problem" || line.state === "unverified");
}

function FoundLibraryProblems({ row }: { row: SuggestedRow }) {
  if (!row.checked || !row.check) return null;
  if (row.check.problem) {
    return (
      <p
        className="mm-status-text--failed text-sm"
        data-testid={`setup-wizard-found-${row.mediaType}-problem`}
      >
        {row.check.problem}
      </p>
    );
  }
  const lines = attentionLines(row.check);
  if (lines.length === 0) {
    return (
      <p className="mm-status-text--healthy text-sm">
        ✓ These folders check out.
      </p>
    );
  }
  return (
    <div className="space-y-1.5">
      <SetupCheckLines label={row.name} lines={lines} />
      <p className="text-xs text-mm-text3">
        You can finish setup now and sort these out afterwards in Settings ›
        Workflows.
      </p>
    </div>
  );
}

function FoundLibrary({
  row,
  managers,
  disabled,
  onChange,
}: {
  row: SuggestedRow;
  managers: MediaManagerConnection[];
  disabled: boolean;
  onChange: Found["edit"];
}) {
  const id = `setup-wizard-found-${row.mediaType}`;
  return (
    <fieldset
      className={`mm-wizard-found${row.checked ? "" : " mm-wizard-found--off"}`}
    >
      <legend className="sr-only">{row.name}</legend>
      <label className="flex cursor-pointer items-start gap-2.5 text-sm font-medium text-mm-text1">
        <input
          type="checkbox"
          className="mt-0.5 h-4 w-4 shrink-0 accent-mm-accent"
          checked={row.checked}
          disabled={disabled}
          onChange={(e) =>
            onChange(row.mediaType, { checked: e.target.checked })
          }
        />
        <span>
          {row.name}
          <span className="ml-2">
            <WorkflowKindBadge
              kind={workflowKindOf(
                { manager_connection_ids: row.managerConnectionIds },
                managers,
              )}
            />
          </span>
          <span className="font-normal text-mm-text2">
            folders from {row.sourceLabel}
          </span>
        </span>
      </label>
      <FolderInput
        id={`${id}-watched`}
        label={`${row.name} watched folder`}
        visibleLabel="Watched folder"
        hint="Where finished downloads land"
        value={row.watched}
        disabled={disabled || !row.checked}
        onChange={(watched) => onChange(row.mediaType, { watched })}
      />
      <FolderInput
        id={`${id}-output`}
        label={`${row.name} output folder`}
        visibleLabel="Output folder"
        hint="Where cleaned files go"
        value={row.output}
        disabled={disabled || !row.checked}
        onChange={(output) => onChange(row.mediaType, { output })}
      />
      <FoundLibraryProblems row={row} />
    </fieldset>
  );
}

/**
 * The libraries Weir found from what is connected: each one a tick with folders to confirm or change, and what
 * the folder check says about it. Nothing is created until Finish.
 */
export function WizardFoundLibraries({
  found,
  managers,
  disabled,
  onNeither,
}: {
  found: Found;
  /** The media managers connected in this setup, to name what each library is linked to. */
  managers: MediaManagerConnection[];
  disabled: boolean;
  /** Gives up on connecting and goes to typing the folders. */
  onNeither: () => void;
}) {
  const { status, rows, notes } = found;

  if (status.isLoading) {
    return <p className="mm-quiet-note">Asking what you connected…</p>;
  }
  if (status.isError) {
    return (
      <div className="space-y-2" role="alert">
        <p className="mm-status-text--failed text-sm">
          Weir could not ask what you connected for its folders just now.
        </p>
        <button
          type="button"
          className={mmActionButtonClass({ variant: "secondary" })}
          onClick={() => void status.refetch()}
        >
          Try again
        </button>
      </div>
    );
  }
  if (!status.data) return null;

  return (
    <div className="flex flex-col gap-3" data-testid="setup-wizard-found">
      {notes.map((note) => (
        <p key={note} className="mm-quiet-note">
          {note}
        </p>
      ))}
      {rows.length === 0 ? (
        <p className="mm-quiet-note">
          Weir could not find a folder to start a workflow from.{" "}
          <button type="button" className="mm-quiet-link" onClick={onNeither}>
            Pick the folders yourself
          </button>
          , or connect something that knows where downloads are saved.
        </p>
      ) : (
        <>
          <h3 className="text-sm font-semibold text-mm-text1">
            Workflows Weir found
          </h3>
          <div className="mm-wizard-libraries">
            {rows.map((row) => (
              <FoundLibrary
                key={row.mediaType}
                row={row}
                managers={managers}
                disabled={disabled}
                onChange={found.edit}
              />
            ))}
          </div>
        </>
      )}
    </div>
  );
}
