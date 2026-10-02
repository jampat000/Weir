import { QuietSection } from "../../../../components/shared/quiet-section";
import { PageToolbarAddButton } from "../../../../components/shell/page-toolbar-actions";
import { Chip } from "../../../../components/panels/chip";
import { WorkflowKindSummary } from "../../../../components/shared/workflow-kind";
import { MmOnOffSwitch } from "../../../../components/ui/mm-on-off-switch";
import type { MediaManagerConnection } from "../../../../lib/media-managers/media-managers-api";
import {
  processingMediaTypeBadge,
  type ProcessingLibrary,
} from "../../../../lib/processing/libraries-api";
import { type ProcessingRuleSet } from "../../../../lib/processing/rule-sets-api";
import {
  workflowKindCounts,
  workflowKindOf,
} from "../../../../lib/processing/workflow-kind";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";

/**
 * The kind of workflow, said the same way on every page: Weir only, or linked to a media manager (and kept in
 * step with it when it came from one). The manager's own last word shows when it did not answer.
 */
function WorkflowSource({
  library,
  connections,
}: {
  library: ProcessingLibrary;
  connections: MediaManagerConnection[];
}) {
  const kind = workflowKindOf(library, connections);
  const unreachable = library.manager_coverage === "unreachable";
  const lastWord = library.manager_connection_ids
    .map((id) => connections.find((c) => c.id === id)?.last_test_detail)
    .find((detail) => detail);
  return (
    <>
      <WorkflowKindSummary kind={kind} />
      {library.discovered_from_connection_id ? (
        <span className="mm-quiet-table__sub">Kept in step with it.</span>
      ) : null}
      {unreachable ? (
        <span className="mm-quiet-table__sub mm-status-text--failed">
          {lastWord ?? "Its media manager did not answer the last check."}
        </span>
      ) : null}
    </>
  );
}

function LibraryFolders({ library }: { library: ProcessingLibrary }) {
  return (
    <>
      <span className="mm-quiet-table__strong mm-library-path">
        {library.watched_folder || "No watched folder yet"}
      </span>
      {library.output_folder ? (
        <span className="mm-quiet-table__sub mm-library-path">
          cleaned into {library.output_folder}
        </span>
      ) : (
        <span className="mm-quiet-table__sub mm-status-text--warning">
          Needs a folder to clean files into
          {library.enabled ? "" : ", so it is off"}
        </span>
      )}
      {library.active_job_count > 0 ? (
        <span className="mm-quiet-table__sub">
          {library.active_job_count} in progress
        </span>
      ) : null}
    </>
  );
}

export type LibraryRowActions = {
  onToggle: (library: ProcessingLibrary) => void;
  onMove: (library: ProcessingLibrary, direction: -1 | 1) => void;
  onEdit: (library: ProcessingLibrary) => void;
  onUnlink: (library: ProcessingLibrary) => void;
  onRemove: (library: ProcessingLibrary) => void;
  unlinking: boolean;
};

function LibraryRow({
  library,
  first,
  last,
  ruleSetName,
  connections,
  editable,
  actions,
}: {
  library: ProcessingLibrary;
  first: boolean;
  last: boolean;
  ruleSetName: string | undefined;
  connections: MediaManagerConnection[];
  editable: boolean;
  actions: LibraryRowActions;
}) {
  const badge = processingMediaTypeBadge(library);
  const tertiary = mmActionButtonClass({ variant: "tertiary" });
  const secondary = mmActionButtonClass({ variant: "secondary" });
  return (
    <tr data-testid={`processing-library-${library.id}`}>
      <th scope="row" className="mm-quiet-table__name">
        <span>{library.name}</span>
        {badge ? <Chip dot={false}>{badge}</Chip> : null}
      </th>
      <td data-label="Kind">
        <WorkflowSource library={library} connections={connections} />
      </td>
      <td data-label="Watches, cleans into">
        <LibraryFolders library={library} />
      </td>
      <td data-label="Rules">
        {ruleSetName ?? (
          <span className="mm-quiet-table__sub">Default rules</span>
        )}
      </td>
      <td data-label="On">
        <MmOnOffSwitch
          id={`processing-library-enabled-${library.id}`}
          label={`${library.name} enabled`}
          enabled={library.enabled}
          disabled={!editable}
          onChange={() => actions.onToggle(library)}
          layout="control"
        />
      </td>
      <td data-label="">
        <div className="flex flex-wrap items-center justify-end gap-2">
          <button
            type="button"
            className={tertiary}
            onClick={() => actions.onMove(library, -1)}
            disabled={!editable || first}
            aria-label={`Move ${library.name} up`}
          >
            ↑
          </button>
          <button
            type="button"
            className={tertiary}
            onClick={() => actions.onMove(library, 1)}
            disabled={!editable || last}
            aria-label={`Move ${library.name} down`}
          >
            ↓
          </button>
          <button
            type="button"
            className={secondary}
            onClick={() => actions.onEdit(library)}
            disabled={!editable}
          >
            Edit
          </button>
          {library.discovered_from_connection_id ? (
            <button
              type="button"
              className={secondary}
              onClick={() => actions.onUnlink(library)}
              disabled={actions.unlinking}
            >
              Unlink
            </button>
          ) : null}
          <button
            type="button"
            className={tertiary}
            onClick={() => actions.onRemove(library)}
            disabled={!editable}
            aria-haspopup="dialog"
            data-testid={`processing-library-remove-${library.id}`}
          >
            Remove
          </button>
        </div>
      </td>
    </tr>
  );
}

/** Every library in the order Weir offers them work, with what each one does. */
export function LibraryListSection({
  libraries,
  ruleSets,
  connections,
  editable,
  onAdd,
  actions,
}: {
  /** Already in display order. */
  libraries: ProcessingLibrary[];
  ruleSets: ProcessingRuleSet[];
  connections: MediaManagerConnection[];
  editable: boolean;
  onAdd: () => void;
  actions: LibraryRowActions;
}) {
  return (
    <QuietSection headingId="processing-libraries-heading" heading="Workflows">
      {editable ? (
        <PageToolbarAddButton
          label="Add workflow"
          onClick={onAdd}
          dataTestId="processing-library-add"
        />
      ) : null}
      <p className="mm-quiet-note">
        A workflow is the path new files take: a folder Weir watches, and a
        folder it writes cleaned files to. It is either{" "}
        <strong>Weir only</strong>, with nothing else involved, or{" "}
        <strong>linked to a media manager</strong>, which hands files over or
        imports the result. Both kinds can sit side by side. A 4K workflow and a
        kids workflow are separate workflows.
      </p>
      {libraries.length === 0 ? (
        <p className="mm-quiet-note mt-4">
          No workflows yet. Add one to tell Weir which folder to watch.
        </p>
      ) : (
        <>
          <p className="mm-quiet-note mt-4" data-testid="workflow-kind-counts">
            {workflowKindCounts(
              libraries.map((library) => workflowKindOf(library, connections)),
            )}
          </p>
          <div className="mm-quiet-table-wrap mt-3">
            <table className="mm-quiet-table">
              <thead>
                <tr>
                  <th scope="col">Workflow</th>
                  <th scope="col">Kind</th>
                  <th scope="col">Watches, cleans into</th>
                  <th scope="col">Rules</th>
                  <th scope="col">On</th>
                  <th scope="col">
                    <span className="sr-only">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {libraries.map((library, index) => (
                  <LibraryRow
                    key={library.id}
                    library={library}
                    first={index === 0}
                    last={index === libraries.length - 1}
                    ruleSetName={
                      ruleSets.find((set) => set.id === library.rule_set_id)
                        ?.name
                    }
                    connections={connections}
                    editable={editable}
                    actions={actions}
                  />
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </QuietSection>
  );
}
