import { useMemo, useState } from "react";

import { SettingRow } from "../../../../components/shared/settings-group";
import { MmListboxPicker } from "../../../../components/ui/mm-listbox-picker";
import { errorMessage } from "../../../../lib/api/error-message";
import { useAppSettingsSaveMutation } from "../../../../lib/settings/queries";
import {
  CURATED_TIMEZONE_ID_SET,
  curatedTimezoneOptionsSorted,
} from "../../../../lib/settings/timezone-options";
import type { AppSettings } from "../../../../lib/settings/types";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { DAY_NAMES, zoneClock } from "./schedule-model";

const FALLBACK_ZONE = "UTC";

const twoDigits = (value: number) => String(value).padStart(2, "0");

/** The saved zone when it is one this picker offers, otherwise nothing chosen yet. */
export function savedZone(settings: AppSettings): string {
  return CURATED_TIMEZONE_ID_SET.has(settings.app_timezone || "")
    ? settings.app_timezone
    : "";
}

/**
 * The time zone, on the Schedule tab because every time on it is read in this zone. The parent keys
 * this row on the saved zone, so a save elsewhere resets the choice.
 */
export function TimeZoneRow({
  settings,
  editable,
  now,
}: {
  settings: AppSettings;
  editable: boolean;
  now: Date;
}) {
  const save = useAppSettingsSaveMutation();
  const saved = savedZone(settings);
  const [zone, setZone] = useState(saved);
  const options = useMemo(() => curatedTimezoneOptionsSorted(), []);
  const clock = zoneClock(now, settings.app_timezone || FALLBACK_ZONE);
  const dirty = zone !== saved && zone !== "";

  return (
    <SettingRow
      label={<span id="schedule-timezone-label">Time zone</span>}
      hint={
        <>
          It is {DAY_NAMES[clock.weekday]} {twoDigits(clock.hour)}:
          {twoDigits(clock.minute)} there now. Every time on this page, and
          across Weir, is in this zone.
        </>
      }
    >
      <div className="mm-schedule-zone">
        <MmListboxPicker
          ariaLabelledBy="schedule-timezone-label"
          placeholder="Select time zone"
          disabled={!editable || save.isPending}
          options={options.map((tz) => ({ value: tz.id, label: tz.label }))}
          value={zone}
          onChange={setZone}
        />
        <button
          type="button"
          className={mmActionButtonClass({ variant: "primary" })}
          disabled={!editable || !dirty || save.isPending}
          data-testid="schedule-save-timezone"
          onClick={() =>
            save.mutate({
              product_display_name: settings.product_display_name,
              signed_in_home_notice: settings.signed_in_home_notice,
              setup_wizard_state: settings.setup_wizard_state,
              app_timezone: zone,
              log_retention_days: settings.log_retention_days,
            })
          }
        >
          {save.isPending ? "Saving…" : "Save"}
        </button>
      </div>
      {save.isError ? (
        <p className="mm-status-text--failed mt-2 text-sm" role="alert">
          {errorMessage(save.error, "The time zone could not be saved.")}
        </p>
      ) : null}
    </SettingRow>
  );
}
