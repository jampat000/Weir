import { useState } from "react";

import { QuietSection } from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import { fetchServerLogDownload } from "../../../../lib/settings/settings-api";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { saveBlobAs } from "../../../../lib/ui/save-file";

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
    <QuietSection
      level={3}
      headingId="suite-settings-logs-download-heading"
      heading="Download the whole log"
    >
      <p className="mm-quiet-note">
        The list above shows the most recent matches. This downloads the whole
        server log as a file, for handing to someone helping with a problem.
      </p>
      <div className="mt-3">
        <button
          type="button"
          className={mmActionButtonClass({ variant: "secondary" })}
          disabled={busy}
          onClick={() => void download()}
        >
          {busy ? "Downloading..." : "Download server log"}
        </button>
      </div>
      {problem ? (
        <p className="mm-status-text--failed mt-2 text-sm" role="alert">
          {problem}
        </p>
      ) : null}
    </QuietSection>
  );
}
