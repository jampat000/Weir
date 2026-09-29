import type { useSuggestedLibraries } from "./use-suggested-libraries";
import type { WizardConnections } from "./use-wizard-connections";
import type { LibraryFolders, WizardDraft } from "./use-wizard-save";
import { WizardConnect } from "./wizard-connect";
import { WizardFoundLibraries } from "./wizard-found-libraries";
import { WizardManualLibraries } from "./wizard-manual-libraries";
import { WizardSourceChoice } from "./wizard-source-choice";
import { isConnectedSource, type DownloadSource } from "./wizard-source";

/**
 * How downloads reach Weir, and the libraries that follow from the answer: connect to what delivers them and
 * confirm the libraries it suggests, or, for anyone with nothing to connect, type the folders.
 */
export function WizardDownloadsSection({
  source,
  onSource,
  connections,
  found,
  draft,
  onFolders,
  disabled,
}: {
  source: DownloadSource | null;
  onSource: (source: DownloadSource | null) => void;
  connections: WizardConnections;
  found: ReturnType<typeof useSuggestedLibraries>;
  draft: Pick<WizardDraft, "movie" | "tv">;
  onFolders: (key: "movie" | "tv", folders: LibraryFolders) => void;
  disabled: boolean;
}) {
  return (
    <div className="flex flex-col gap-4">
      <WizardSourceChoice
        value={source}
        disabled={disabled}
        onChange={onSource}
      />
      {isConnectedSource(source) ? (
        <WizardConnect
          source={source}
          connections={connections}
          onBack={() => onSource(null)}
        />
      ) : null}
      {isConnectedSource(source) && connections.answering.length > 0 ? (
        <WizardFoundLibraries
          found={found}
          managers={connections.managers}
          disabled={disabled}
          onNeither={() => onSource("neither")}
        />
      ) : null}
      {source === "neither" ? (
        <>
          <p className="mm-quiet-note">
            Where your downloader finishes files, and where Weir puts them once
            cleaned for your media manager to import. This fills in your first
            Movies and TV workflow. Add more workflows in Settings › Workflows,
            and choose which tracks to keep in Settings › Rules.
          </p>
          <WizardManualLibraries
            draft={draft}
            disabled={disabled}
            onChange={onFolders}
          />
        </>
      ) : null}
    </div>
  );
}
