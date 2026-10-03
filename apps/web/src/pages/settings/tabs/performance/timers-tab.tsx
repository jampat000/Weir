import { useId } from "react";

import { PageLoading } from "../../../../components/shared/page-loading";
import { useAppSettingsQuery } from "../../../../lib/settings/queries";
import { SettingsLoadError } from "../../settings-load-error";
import { TimersSection } from "./timers-section";

/** Setup › Performance › Weir's timers: when Weir's own jobs run, which no workflow's hours change. */
export function TimersTab() {
  const settings = useAppSettingsQuery();
  const headingId = useId();

  if (settings.isPending) return <PageLoading label="Loading timers" />;
  if (settings.isError) return <SettingsLoadError what="timers" />;

  return (
    <div className="mm-quiet-stack" data-testid="processing-timers-section">
      <TimersSection headingId={headingId} settings={settings.data} />
    </div>
  );
}
