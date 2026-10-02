import { Link } from "react-router-dom";

import { useProcessingOperatorSettingsQuery } from "../../lib/processing/queries";
import { plural } from "../../lib/ui/mm-plural";
import { RETENTION_PATH } from "../system/tabs/logs/retention-section";

/** How long a file's activity is kept, in the few words Activity has room for. */
function keptWords(days: number): string {
  return days > 0
    ? `File activity is kept ${plural(days, "day", "days")} after a file is gone`
    : "File activity is kept until the file is removed";
}

/** A line under Activity saying how long its records are kept, with the way to change it. */
export function ActivityRetentionNote() {
  const settings = useProcessingOperatorSettingsQuery();
  if (!settings.data) return null;
  return (
    <p className="mm-history-note" data-testid="activity-retention">
      {keptWords(settings.data.file_log_retention_days)} ·{" "}
      <Link to={RETENTION_PATH}>change</Link>
    </p>
  );
}
