import { QuietSection } from "../../../../components/shared/quiet-section";
import { MmOnOffSwitch } from "../../../../components/ui/mm-on-off-switch";
import type { MediaManagerConnection } from "../../../../lib/media-managers/media-managers-api";
import {
  processingMediaTypeBadge,
  type ProcessingLibrary,
  type ProcessingRuleSet,
} from "../../../../lib/processing/libraries-api";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";

/**
 * Where a library comes from, said plainly: from a media manager and kept in step with it, or made
 * here and linked to one (or not). The manager's own last word shows when it did not answer.
 */
function LibrarySource({
  library,
  connections,
}: {
  library: ProcessingLibrary;
  connections: MediaManagerConnection[];
}) {
  const nameOf = (id: number) =>
    connections.find((c) => c.id === id)?.name ?? `manager #${id}`;
  const linked = library.manager_connection_ids;
  const unreachable = library.manager_coverage === "unreachable";
  const lastWord = linked
    .map((id) => connections.find((c) => c.id === id)?.last_test_detail)
    .find((detail) => detail);
  return (
    <>
      {library.discovered_from_connection_id ? (
        <span className="mm-quiet-table__strong mm-library-source mm-library-source--synced">
          From {nameOf(library.discovered_from_connection_id)}
        </span>
      ) : (
        <span className="mm-quiet-table__strong mm-library-source">
          Added in Weir
        </span>
      )}
      <span className="mm-quiet-table__sub">
        {library.discovered_from_connection_id
          ? "kept in step with it"
          : linked.length > 0
            ? `linked to ${linked.map(nameOf).join(", ")}`
            : "not linked to a media manager"}
      </span>
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
          hands back to {library.output_folder}
        </span>
      ) : (
        <span className="mm-quiet-table__sub mm-status-text--warning">
          Needs a folder to hand files back to
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
        {badge ? <span className="mm-quiet-badge">{badge}</span> : null}
      </th>
      <td data-label="Source">
        <LibrarySource library={library} connections={connections} />
      </td>
      <td data-label="Watches, hands back to">
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
    <QuietSection
      headingId="processing-libraries-heading"
      heading="Libraries"
      aside={
        editable ? (
          <button
            type="button"
            className="mm-quiet-link"
            onClick={onAdd}
            data-testid="processing-library-add"
          >
            Add library →
          </button>
        ) : null
      }
    >
      <p className="mm-quiet-note">
        A library is a folder Weir watches and a folder it hands clean files
        back to. A library from a media manager is kept in step with it; one
        added in Weir is yours alone, and can still be linked to a manager in
        its editor. A 4K library and a kids library are separate libraries.
      </p>
      {libraries.length === 0 ? (
        <p className="mm-quiet-note mt-4">
          No libraries yet. Add one to tell Weir which folder to watch.
        </p>
      ) : (
        <div className="mm-quiet-table-wrap mt-5">
          <table className="mm-quiet-table">
            <thead>
              <tr>
                <th scope="col">Library</th>
                <th scope="col">Source</th>
                <th scope="col">Watches, hands back to</th>
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
                    ruleSets.find((set) => set.id === library.rule_set_id)?.name
                  }
                  connections={connections}
                  editable={editable}
                  actions={actions}
                />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </QuietSection>
  );
}
