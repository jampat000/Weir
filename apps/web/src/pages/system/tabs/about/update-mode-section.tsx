import { useEffect, useState } from "react";

import { LoadError } from "../../../../components/shared/load-error";
import {
  QuietSection,
  quietActionRowClass,
} from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import {
  useUpdateSettingsMutation,
  useUpdateSettingsQuery,
} from "../../../../lib/settings/queries";
import type {
  UpdateMode,
  UpdateSettingsOut,
} from "../../../../lib/settings/types";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";

const UPDATE_MODES: {
  value: UpdateMode;
  label: string;
  description: string;
}[] = [
  {
    value: "Auto",
    label: "Auto",
    description:
      "Download and install updates automatically. You will be prompted to restart.",
  },
  {
    value: "DownloadOnly",
    label: "Download only",
    description:
      "Download updates silently in the background, then notify you when ready to install.",
  },
  {
    value: "NotifyOnly",
    label: "Notify only",
    description:
      "Alert you when an update is available without downloading anything.",
  },
];

const CHECK_INTERVALS = [
  { value: 15, label: "Every 15 minutes" },
  { value: 30, label: "Every 30 minutes" },
  { value: 60, label: "Every hour" },
  { value: 120, label: "Every 2 hours" },
  { value: 360, label: "Every 6 hours" },
  { value: 720, label: "Every 12 hours" },
  { value: 1440, label: "Every 24 hours" },
];

const DEFAULT_CHECK_INTERVAL_MINUTES = 60;

type Draft = Omit<UpdateSettingsOut, "mode"> & { mode: UpdateMode | null };

const EMPTY_DRAFT: Draft = {
  mode: null,
  check_on_startup: true,
  check_interval_minutes: DEFAULT_CHECK_INTERVAL_MINUTES,
};

function ModeChoices({
  selected,
  onSelect,
}: {
  selected: UpdateMode | null;
  onSelect: (mode: UpdateMode) => void;
}) {
  return (
    <fieldset className="divide-y divide-mm-border">
      <legend className="sr-only">Update mode</legend>
      {UPDATE_MODES.map((opt) => (
        <label
          key={opt.value}
          className={`mm-update-mode${selected === opt.value ? " mm-update-mode--chosen" : ""}`}
        >
          <input
            type="radio"
            name="update-mode"
            value={opt.value}
            checked={selected === opt.value}
            onChange={() => onSelect(opt.value)}
            className="mt-0.5 h-4 w-4 shrink-0 accent-mm-accent"
          />
          <span className="min-w-0">
            <span className="block font-medium">{opt.label}</span>
            <span className="block text-xs text-mm-text3">
              {opt.description}
            </span>
          </span>
        </label>
      ))}
    </fieldset>
  );
}

/** How the Windows tray app handles updates: its own form, with its own Save. */
export function UpdateModeSection() {
  const settingsQ = useUpdateSettingsQuery(true);
  const saveMode = useUpdateSettingsMutation();
  const [draft, setDraft] = useState<Draft>(EMPTY_DRAFT);
  const [saved, setSaved] = useState(false);
  const server = settingsQ.data;

  useEffect(() => {
    if (server) setDraft(server);
  }, [server]);

  const dirty =
    draft.mode !== null &&
    (draft.mode !== (server?.mode ?? null) ||
      draft.check_on_startup !== (server?.check_on_startup ?? true) ||
      draft.check_interval_minutes !==
        (server?.check_interval_minutes ?? DEFAULT_CHECK_INTERVAL_MINUTES));

  const change = (next: Partial<Draft>) => {
    setDraft((prev) => ({ ...prev, ...next }));
    setSaved(false);
    saveMode.reset();
  };

  const saveDraft = () => {
    if (!draft.mode || !server) return;
    setSaved(false);
    saveMode.reset();
    saveMode.mutate(
      { ...draft, mode: draft.mode },
      { onSuccess: () => setSaved(true) },
    );
  };

  return (
    <QuietSection
      level={3}
      headingId="suite-settings-upgrade-mode-heading"
      heading="Update mode"
    >
      <p className="mm-quiet-note">
        Choose how the Weir tray app handles available updates.
      </p>
      {settingsQ.isPending ? (
        <p className="mm-quiet-note mt-3">Loading update preferences...</p>
      ) : settingsQ.isError ? (
        <div className="mt-3">
          <LoadError thing="update preferences" error={settingsQ.error} />
        </div>
      ) : (
        <div className="mt-4 max-w-2xl space-y-5">
          <ModeChoices
            selected={draft.mode}
            onSelect={(mode) => change({ mode })}
          />
          <div className="space-y-3">
            <label className="flex cursor-pointer items-center gap-2 text-sm text-mm-text2">
              <input
                type="checkbox"
                className="h-4 w-4 shrink-0 accent-mm-accent"
                checked={draft.check_on_startup}
                onChange={(e) => change({ check_on_startup: e.target.checked })}
              />
              Check for updates on startup
            </label>
            <label className="block text-sm text-mm-text2">
              <span className="mb-1.5 block text-sm text-mm-text2">
                Check interval
              </span>
              <select
                className="mm-input w-full max-w-xs"
                value={draft.check_interval_minutes}
                onChange={(e) =>
                  change({ check_interval_minutes: Number(e.target.value) })
                }
              >
                {CHECK_INTERVALS.map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </select>
            </label>
          </div>
        </div>
      )}

      {saveMode.isError ? (
        <p className="mm-status-text--failed text-sm" role="alert">
          {errorMessage(saveMode.error, "Could not save update settings.")}
        </p>
      ) : null}
      {saved ? (
        <p className="mm-status-text--healthy mt-3 text-sm">
          Update settings saved.
        </p>
      ) : null}

      <div className={`mt-5 ${quietActionRowClass}`}>
        <button
          type="button"
          className={mmActionButtonClass({ variant: "primary" })}
          disabled={!dirty || saveMode.isPending || settingsQ.isPending}
          onClick={saveDraft}
        >
          {saveMode.isPending ? "Saving..." : "Save"}
        </button>
        {dirty ? (
          <button
            type="button"
            className={mmActionButtonClass({ variant: "secondary" })}
            disabled={saveMode.isPending}
            onClick={() => change(server ?? EMPTY_DRAFT)}
          >
            Discard
          </button>
        ) : null}
      </div>
    </QuietSection>
  );
}
