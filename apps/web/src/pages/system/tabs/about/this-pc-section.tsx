import { Panel } from "../../../../components/panels/panel";
import type { AppSettings } from "../../../../lib/settings/types";
import { useNow } from "../../../../lib/ui/use-now";
import {
  savedZone,
  TimeZoneRow,
} from "../../../settings/tabs/schedule/time-zone-row";
import { NetworkAccessSetting } from "./network-access-setting";

/** Often enough that "now 14:32" stays true while the page is open. */
const CLOCK_TICK_MS = 30_000;
const NETWORK_LABEL_ID = "about-network-label";
const TIME_ZONE_LABEL_ID = "about-time-zone-label";

/** System › About: who can reach this computer and what time Weir keeps on it, in two halves that line up. */
export function ThisPcSection({
  settings,
  editable,
  onTimeZoneUnsavedChange,
}: {
  settings: AppSettings;
  editable: boolean;
  /** Tells the page whether a zone has been picked and not yet saved. */
  onTimeZoneUnsavedChange: (unsaved: boolean) => void;
}) {
  const now = new Date(useNow(CLOCK_TICK_MS));

  return (
    <Panel
      title="This PC"
      headingId="about-this-pc-heading"
      headingLevel={3}
      padded
      dataTestId="about-time-zone"
    >
      <div className="mm-this-pc">
        <section
          className="mm-this-pc__half"
          aria-labelledby={NETWORK_LABEL_ID}
          data-testid="about-network-access"
        >
          <h4 id={NETWORK_LABEL_ID} className="mm-this-pc__label">
            Network
          </h4>
          <NetworkAccessSetting editable={editable} />
        </section>
        <section
          className="mm-this-pc__half"
          aria-labelledby={TIME_ZONE_LABEL_ID}
        >
          <h4 id={TIME_ZONE_LABEL_ID} className="mm-this-pc__label">
            Time zone
          </h4>
          <TimeZoneRow
            key={savedZone(settings)}
            labelId={TIME_ZONE_LABEL_ID}
            settings={settings}
            editable={editable}
            now={now}
            onUnsavedChange={onTimeZoneUnsavedChange}
          />
        </section>
      </div>
    </Panel>
  );
}
