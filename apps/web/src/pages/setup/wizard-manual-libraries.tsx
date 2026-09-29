import { FolderInput } from "./setup-wizard-parts";
import type { LibraryFolders, WizardDraft } from "./use-wizard-save";

const LIBRARY_GROUPS = [
  { key: "movie", heading: "Movies" },
  { key: "tv", heading: "TV" },
] as const;

/** The folders typed by hand for the Movies and TV libraries: setup for anyone with no manager to connect. */
export function WizardManualLibraries({
  draft,
  disabled,
  onChange,
}: {
  draft: Pick<WizardDraft, "movie" | "tv">;
  disabled: boolean;
  onChange: (key: "movie" | "tv", folders: LibraryFolders) => void;
}) {
  return (
    <div className="mm-wizard-libraries">
      {LIBRARY_GROUPS.map((group) => (
        <fieldset key={group.key} className="mm-wizard-library">
          <legend className="mm-wizard-library__title">{group.heading}</legend>
          <FolderInput
            id={`setup-wizard-${group.key}-watched`}
            label={`${group.heading} watched folder`}
            visibleLabel="Watched folder"
            hint="Where finished downloads land"
            value={draft[group.key].watched}
            disabled={disabled}
            onChange={(watched) =>
              onChange(group.key, { ...draft[group.key], watched })
            }
          />
          <FolderInput
            id={`setup-wizard-${group.key}-output`}
            label={`${group.heading} output folder`}
            visibleLabel="Output folder"
            hint="Where cleaned files go"
            value={draft[group.key].output}
            disabled={disabled}
            onChange={(output) =>
              onChange(group.key, { ...draft[group.key], output })
            }
          />
        </fieldset>
      ))}
    </div>
  );
}
