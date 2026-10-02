import { useState } from "react";

import { Panel } from "../../../../components/panels/panel";
import { errorMessage } from "../../../../lib/api/error-message";
import { fetchServerLogDownload } from "../../../../lib/settings/settings-api";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { saveBlobAs } from "../../../../lib/ui/save-file";

const WHOLE_LOG_DETAIL =
  "The list above shows the most recent matches. This downloads the whole server log as a file, for handing to someone helping with a problem.";

/** The view above only shows the most recent matches; this hands over the whole file. */
export function DownloadLogSection() {
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);

  const download = async () => {
    setBusy(true);
    setProblem(null);
    try {
      const blob = await fetchServerLogDownload();
      const stamp = new Date().toISOString().replace(/[:.]/g, "-");
      saveBlobAs(blob, `weir-log-${stamp}.log`);
    } catch (error) {
      setProblem(errorMessage(error, "Could not download the server log."));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Panel
      title="Download the whole log"
      headingId="suite-settings-logs-download-heading"
      headingLevel={3}
      padded
      aside={
        <button
          type="button"
          className={`${mmActionButtonClass({ variant: "secondary" })} mm-sys-btn`}
          disabled={busy}
          onClick={() => void download()}
        >
          {busy ? "Downloading..." : "Download server log"}
        </button>
      }
    >
      <p className="mm-quiet-note" title={WHOLE_LOG_DETAIL}>
        The whole server log as one file, to hand to whoever is helping.
      </p>
      {problem ? (
        <p className="mm-status-text--failed mm-sys-note" role="alert">
          {problem}
        </p>
      ) : null}
    </Panel>
  );
}
