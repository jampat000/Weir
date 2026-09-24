import { QuietFieldGroup } from "../../../../components/shared/quiet-section";
import { REJECTED_FILE_OPTIONS } from "./library-options";
import {
  DateTimeSetting,
  SelectSetting,
  TextSetting,
  ToggleSetting,
  type LibraryFormBinding,
} from "./library-settings";

/** Which files belong to the library, decided before Weir spends time probing them. */
export function LibraryIntakeGroup({
  binding,
}: {
  binding: LibraryFormBinding;
}) {
  return (
    <QuietFieldGroup
      title="Intake rules"
      detail="Decide which files belong here before Weir spends time probing or processing them. A maximum of 0 means no limit."
    >
      <div className="mm-field-row">
        <TextSetting
          binding={binding}
          name="media_extensions_csv"
          label="File types"
          width="medium"
          placeholder=".mkv,.mp4"
        />
        <TextSetting
          binding={binding}
          name="exclude_markers_csv"
          label="Downloader folders to ignore"
          width="medium"
          placeholder="__admin__,incomplete"
          hint="Comma-separated folder names used while a download is incomplete."
        />
        <TextSetting
          binding={binding}
          name="include_patterns_csv"
          label="Path must match"
          width="medium"
          placeholder="*feature*,Movies/*"
          hint="Optional comma-separated wildcards. Empty accepts every path."
        />
        <TextSetting
          binding={binding}
          name="exclude_patterns_csv"
          label="Path must not match"
          width="medium"
          placeholder="*sample*,*trailer*"
          hint="Optional comma-separated wildcards."
        />
        <TextSetting
          binding={binding}
          name="min_file_size_mb"
          label="Minimum file size (MB)"
          width="short"
          placeholder="0"
        />
        <TextSetting
          binding={binding}
          name="max_file_size_mb"
          label="Maximum file size (MB)"
          width="short"
          placeholder="0"
        />
        <div className="w-full space-y-2">
          <div>
            <p className="mm-library-window__title">
              Created and modified windows
            </p>
            <p className="mm-library-window__detail">
              Optional. Leave a side blank for no limit. Times are shown in this
              browser&apos;s timezone and saved as UTC. Windows uses the file
              creation time; Linux and Docker use the best filesystem
              birth/change time available.
            </p>
          </div>
          <div className="mm-field-row">
            <DateTimeSetting
              binding={binding}
              name="created_after"
              label="Created after"
            />
            <DateTimeSetting
              binding={binding}
              name="created_before"
              label="Created before"
            />
            <DateTimeSetting
              binding={binding}
              name="modified_after"
              label="Modified after"
            />
            <DateTimeSetting
              binding={binding}
              name="modified_before"
              label="Modified before"
            />
          </div>
        </div>
        <SelectSetting
          binding={binding}
          name="rejected_file_action"
          label="When a file is rejected"
          options={REJECTED_FILE_OPTIONS}
          hint="Applies after readiness checks to size and path-rule rejections. Weir never deletes a populated parent folder here."
        />
      </div>
      <div className="mm-library-toggles">
        <ToggleSetting
          binding={binding}
          name="exclude_hidden"
          label="Skip hidden files"
        />
        <ToggleSetting
          binding={binding}
          name="top_level_only"
          label="Only inspect the top folder"
        />
      </div>
    </QuietFieldGroup>
  );
}

/** Checks that stop Weir starting while a downloader or media manager still owns the file. */
export function LibraryReadinessGroup({
  binding,
}: {
  binding: LibraryFormBinding;
}) {
  return (
    <QuietFieldGroup
      title="File readiness"
      detail="These checks prevent Weir from starting while a downloader, recorder, or media manager still owns the file."
    >
      <div className="mm-field-row">
        <TextSetting
          binding={binding}
          name="min_file_age_seconds"
          label="Minimum unchanged age (seconds)"
          width="short"
        />
        <TextSetting
          binding={binding}
          name="hold_minutes"
          label="Hold every new file (minutes)"
          width="short"
        />
        <TextSetting
          binding={binding}
          name="file_detection_interval_seconds"
          label="Size must stay stable (seconds)"
          width="short"
        />
        <TextSetting
          binding={binding}
          name="scan_interval_seconds"
          label="Look for new files every (seconds)"
          width="short"
        />
      </div>
      <div className="mm-library-toggles">
        <ToggleSetting
          binding={binding}
          name="file_system_events_enabled"
          label="Watch this folder for changes"
          hint="The periodic scan remains as a backstop for Docker, SMB, and NFS."
        />
        <ToggleSetting
          binding={binding}
          name="ignore_size_changes"
          label="Ignore size changes"
          hint="Use only when another system guarantees the file is complete."
        />
        <ToggleSetting
          binding={binding}
          name="skip_access_tests"
          label="Skip read and write tests"
          hint="Less safe: locked sources and unwritable outputs may fail after queueing."
        />
      </div>
    </QuietFieldGroup>
  );
}
