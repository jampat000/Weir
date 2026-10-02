import type { ProcessingLibrary } from "../../../../lib/processing/libraries-api";

/** A folder, in the code face it is typed in, cut from its start when it is too long so its last folders show. */
function FolderPath({ path }: { path: string }) {
  return (
    <span className="mm-folder-path" title={path}>
      <bdo dir="ltr">{path}</bdo>
    </span>
  );
}

function Remark({ children }: { children: string }) {
  return (
    <span className="mm-quiet-table__sub mm-folder-remark">{children}</span>
  );
}

/** The folder Weir watches for new files. A workflow that only cleans a library has none. */
export function WatchedFolderCell({ library }: { library: ProcessingLibrary }) {
  return library.watched_folder ? (
    <FolderPath path={library.watched_folder} />
  ) : (
    <Remark>Not watching a folder</Remark>
  );
}

/**
 * The folder cleaned files are written to. A workflow with no folders at all only cleans files where they
 * already are; one that watches a folder but has nowhere to write cannot run.
 */
export function OutputFolderCell({ library }: { library: ProcessingLibrary }) {
  if (library.output_folder) return <FolderPath path={library.output_folder} />;
  if (!library.watched_folder) return <Remark>Cleans in place</Remark>;
  return (
    <span
      className="mm-quiet-table__sub mm-status-text--warning mm-folder-remark"
      title={`Needs a folder to clean files into${library.enabled ? "" : ", so it is off"}.`}
    >
      Needs an output folder
    </span>
  );
}
