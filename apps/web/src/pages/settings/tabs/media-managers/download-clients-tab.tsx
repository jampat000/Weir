import { DownloadClientsSection } from "./download-clients-section";

/** Setup › Connections › Download clients: the bare download clients Weir reads watched-folder suggestions from. */
export function DownloadClientsTab() {
  return (
    <div
      className="mm-quiet-stack"
      data-testid="suite-settings-download-clients-tab"
    >
      <DownloadClientsSection />
    </div>
  );
}
