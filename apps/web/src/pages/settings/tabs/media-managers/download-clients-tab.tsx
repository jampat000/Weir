import { SaveModelNote } from "../../save-model-note";
import { DownloadClientsSection } from "./download-clients-section";

/** Setup › Connections › Download clients: the bare download clients Weir reads watched-folder suggestions from. */
export function DownloadClientsTab() {
  return (
    <div
      className="mm-quiet-stack"
      data-testid="suite-settings-download-clients-tab"
    >
      <SaveModelNote model="instant" />
      <p className="mm-quiet-note">
        Some installs have no media manager at all — just Weir and a download
        client. Connect one here and Weir can suggest a watched folder from it
        too. This connection is for suggestions only: Weir only ever reads the
        download client&apos;s own settings, never changes them, and a folder
        you type yourself always wins.
      </p>
      <DownloadClientsSection />
    </div>
  );
}
