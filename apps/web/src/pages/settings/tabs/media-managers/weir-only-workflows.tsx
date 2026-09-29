import { useProcessingLibrariesQuery } from "../../../../lib/processing/libraries-queries";

/**
 * The workflows no media manager is involved in, so a connection's list of what it feeds is not read as
 * the whole picture. Nothing shows when every workflow is linked, or while they load.
 */
export function WeirOnlyWorkflows() {
  const libraries = useProcessingLibrariesQuery();
  const weirOnly = (libraries.data ?? []).filter(
    (library) => library.manager_connection_ids.length === 0,
  );
  if (weirOnly.length === 0) return null;
  return (
    <p className="mm-quiet-note" data-testid="weir-only-workflows">
      Weir only, with no media manager involved:{" "}
      <span className="font-medium text-mm-text">
        {weirOnly.map((library) => library.name).join(", ")}
      </span>
      . Weir watches their folders and writes cleaned files to their output
      folders by itself.
    </p>
  );
}
