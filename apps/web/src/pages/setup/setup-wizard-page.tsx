import { useEffect, useMemo, useState } from "react";
import { Navigate } from "react-router-dom";

import { AuthBrandStack } from "../../components/brand/auth-brand-stack";
import { PageLoading } from "../../components/shared/page-loading";
import { MmListboxPicker } from "../../components/ui/mm-listbox-picker";
import { useMeQuery } from "../../lib/auth/queries";
import type { ProcessingLibrary } from "../../lib/processing/libraries-api";
import { useProcessingLibrariesQuery } from "../../lib/processing/libraries-queries";
import { useAppSettingsQuery } from "../../lib/settings/queries";
import {
  curatedTimezoneOptionsSorted,
  CURATED_TIMEZONE_ID_SET,
} from "../../lib/settings/timezone-options";
import type { AppSettings } from "../../lib/settings/types";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import {
  BackupFields,
  DEFAULT_BACKUP_TIME,
  FolderInput,
  WizardLoadFailed,
  WizardSection,
} from "./setup-wizard-parts";
import {
  firstLibraryOfType,
  useWizardSave,
  type LibraryFolders,
  type WizardDraft,
} from "./use-wizard-save";

const FALLBACK_ZONE = "UTC";
const DEFAULT_BACKUP_HOURS = 24;
const LIBRARY_GROUPS = [
  { key: "movie", heading: "Movies" },
  { key: "tv", heading: "TV" },
] as const;

function wizardStateOf(settings: AppSettings | undefined): string {
  return (settings?.setup_wizard_state || "pending").trim().toLowerCase();
}

/** The draft starts from what is saved: the zone, the backup schedule and the first library of each type. */
function initialDraft(
  settings: AppSettings,
  libraries: ProcessingLibrary[] | undefined,
): WizardDraft {
  const zone = (settings.app_timezone || FALLBACK_ZONE).trim() || FALLBACK_ZONE;
  const folders = (type: "movie" | "tv"): LibraryFolders => {
    const library = firstLibraryOfType(libraries, type);
    return {
      watched: library?.watched_folder ?? "",
      output: library?.output_folder ?? "",
    };
  };
  return {
    timezone: CURATED_TIMEZONE_ID_SET.has(zone) ? zone : FALLBACK_ZONE,
    backup: {
      enabled: Boolean(settings.configuration_backup_enabled),
      intervalHours: String(
        settings.configuration_backup_interval_hours || DEFAULT_BACKUP_HOURS,
      ),
      preferredTime:
        (
          settings.configuration_backup_preferred_time || DEFAULT_BACKUP_TIME
        ).trim() || DEFAULT_BACKUP_TIME,
    },
    movie: folders("movie"),
    tv: folders("tv"),
  };
}

/** Leaving before the first run is finished or skipped asks the browser to confirm. */
function useLeaveWarning(active: boolean) {
  useEffect(() => {
    if (!active) return undefined;
    const handler = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [active]);
}

function WizardForm({
  settings,
  libraries,
}: {
  settings: AppSettings;
  libraries: ProcessingLibrary[] | undefined;
}) {
  const [draft, setDraft] = useState(() => initialDraft(settings, libraries));
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const { save, pending } = useWizardSave({
    settings,
    libraries,
    onMessage: setStatusMessage,
  });
  const timezoneOptions = useMemo(
    () =>
      curatedTimezoneOptionsSorted().map((tz) => ({
        value: tz.id,
        label: tz.label,
      })),
    [],
  );
  const setFolders = (key: "movie" | "tv", folders: LibraryFolders) =>
    setDraft((current) => ({ ...current, [key]: folders }));

  return (
    <main className="mm-auth-body" id="mm-main-content" tabIndex={-1}>
      <div className="mm-auth-frame mm-setup-wizard-frame">
        <AuthBrandStack />
        <div className="mm-auth-card mm-setup-wizard-card">
          <p className="mm-auth-eyebrow">First run</p>
          <h1 className="mm-auth-title">Set up Weir</h1>
          <p className="mm-auth-lead">
            A few basics to get Weir going. Everything here can be changed later
            in Settings and Processing, and you can skip it for now.
          </p>

          <div className="mm-quiet-stack mt-5">
            <WizardSection
              headingId="setup-wizard-basics-heading"
              title="Basics"
              description="The clock Weir uses for schedules and for everything it writes down."
            >
              <div className="mm-wizard-basics">
                <div className="min-w-0">
                  <span id="setup-wizard-timezone" className="mm-wizard-label">
                    Time zone
                  </span>
                  <MmListboxPicker
                    ariaLabelledBy="setup-wizard-timezone"
                    placeholder="Select time zone"
                    disabled={pending}
                    options={timezoneOptions}
                    value={draft.timezone}
                    onChange={(timezone) =>
                      setDraft((current) => ({ ...current, timezone }))
                    }
                  />
                </div>
              </div>
            </WizardSection>

            <WizardSection
              headingId="setup-wizard-libraries-heading"
              title="Libraries"
              description="Where your downloader finishes files, and where Weir puts them once cleaned for your media manager to import. This fills in your first Movies and TV library. Add more under Settings › Libraries, and set their audio and subtitle rules under Settings › Rules."
            >
              <div className="mm-wizard-libraries">
                {LIBRARY_GROUPS.map((group) => (
                  <fieldset key={group.key} className="mm-wizard-library">
                    <legend className="mm-wizard-library__title">
                      {group.heading}
                    </legend>
                    <FolderInput
                      id={`setup-wizard-${group.key}-watched`}
                      label={`${group.heading} watched folder`}
                      visibleLabel="Watched folder"
                      hint="Where finished downloads land"
                      value={draft[group.key].watched}
                      disabled={pending}
                      onChange={(watched) =>
                        setFolders(group.key, { ...draft[group.key], watched })
                      }
                    />
                    <FolderInput
                      id={`setup-wizard-${group.key}-output`}
                      label={`${group.heading} output folder`}
                      visibleLabel="Output folder"
                      hint="Where cleaned files go"
                      value={draft[group.key].output}
                      disabled={pending}
                      onChange={(output) =>
                        setFolders(group.key, { ...draft[group.key], output })
                      }
                    />
                  </fieldset>
                ))}
              </div>
            </WizardSection>

            <WizardSection
              headingId="setup-wizard-backups-heading"
              title="Automatic backups"
              description="Keep a rolling local copy of your Weir configuration."
            >
              <BackupFields
                draft={draft.backup}
                onChange={(backup) =>
                  setDraft((current) => ({ ...current, backup }))
                }
              />
            </WizardSection>
          </div>

          {statusMessage ? (
            // The gap goes on a wrapper: `.mm-auth-banner` sets its own margin, which
            // beats an `mt-4` on the banner itself and leaves it flush.
            <div className="mt-4">
              <p className="mm-auth-banner" role="alert">
                {statusMessage}
              </p>
            </div>
          ) : null}

          <div className="mt-4 flex flex-wrap gap-3">
            <button
              type="button"
              className="mm-auth-submit"
              onClick={() => void save(draft, "completed")}
              disabled={pending}
            >
              {pending
                ? "Saving..."
                : wizardStateOf(settings) === "pending"
                  ? "Finish setup"
                  : "Save changes"}
            </button>
            <button
              type="button"
              data-testid="setup-wizard-skip"
              className={mmActionButtonClass({ variant: "secondary" })}
              onClick={() => void save(draft, "skipped")}
              disabled={pending}
            >
              Skip for now
            </button>
          </div>
        </div>
      </div>
    </main>
  );
}

export function SetupWizardPage() {
  const me = useMeQuery();
  const settingsQ = useAppSettingsQuery();
  const processingQ = useProcessingLibrariesQuery();
  const loading = me.isPending || settingsQ.isPending || processingQ.isPending;
  useLeaveWarning(!loading && wizardStateOf(settingsQ.data) === "pending");

  if (loading) return <PageLoading label="Loading setup wizard" />;
  if (!me.data) return <Navigate to="/login" replace />;
  if (settingsQ.isError || !settingsQ.data) return <WizardLoadFailed />;
  return <WizardForm settings={settingsQ.data} libraries={processingQ.data} />;
}
