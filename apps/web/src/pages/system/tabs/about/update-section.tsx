import { useState } from "react";

import { Chip } from "../../../../components/panels/chip";
import { Panel } from "../../../../components/panels/panel";
import { errorMessage } from "../../../../lib/api/error-message";
import {
  useUpdateSettingsQuery,
  useUpdateStatusQuery,
} from "../../../../lib/settings/queries";
import type { UpdateStatus } from "../../../../lib/settings/types";
import { updateMeaning } from "../../../../lib/settings/update-status";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { useAppDateFormatter } from "../../../../lib/ui/mm-format-date";
import { updateStatusLabel } from "./update-words";
import { UpdatePreferences } from "./update-preferences";
import { UpdateReadyNotice } from "./update-ready-notice";

const COPIED_MS = 2000;

function CopyCommandButton({ command }: { command: string }) {
  const [copied, setCopied] = useState(false);
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(command);
      setCopied(true);
      window.setTimeout(() => setCopied(false), COPIED_MS);
    } catch {
      setCopied(false);
    }
  };
  return (
    <button
      type="button"
      className="mm-quiet-link"
      onClick={() => void copy()}
      aria-label="Copy the update command"
    >
      {copied ? "Copied" : "Copy →"}
    </button>
  );
}

/** The version the status is about: the new one when there is one, otherwise the one running. */
function statusVersion(status: UpdateStatus): string | null {
  if (status.status === "update_available")
    return status.latest_version ?? null;
  if (status.status === "up_to_date") return status.current_version;
  return null;
}

/** While GitHub is limiting the checks, the newest release Weir had learned of before that. */
function LastKnownRelease({ status }: { status: UpdateStatus }) {
  const formatDate = useAppDateFormatter();
  if (status.status !== "rate_limited" || !status.latest_version) return null;
  return (
    <p
      className="mm-quiet-note"
      data-testid="suite-settings-last-known-release"
    >
      Latest known: {status.latest_version}
      {status.published_at
        ? `, published ${formatDate(status.published_at)}`
        : ""}
    </p>
  );
}

function ReleaseStatus({
  status,
  checking,
  isWindows,
  dockerCommand,
  showUpdateButton,
  onCheck,
}: {
  status: UpdateStatus;
  checking: boolean;
  isWindows: boolean;
  /** The command a Docker install runs to take the update, when there is an update and it is one. */
  dockerCommand: string | null;
  /** Notify-only mode never downloads anything itself, so the installer link needs to read as the action, not a footnote. */
  showUpdateButton: boolean;
  onCheck: () => void;
}) {
  const links = [
    { label: "Download installer →", href: status.windows_installer_url },
    { label: "Release notes →", href: status.release_url },
  ].filter((link) => link.href);
  const version = statusVersion(status);

  return (
    <Panel
      title="Updates"
      headingId="suite-settings-upgrade-heading"
      headingLevel={3}
      padded
      dataTestId="suite-settings-upgrade-tab"
      aside={
        <>
          <button
            type="button"
            className="mm-quiet-link"
            disabled={checking}
            onClick={onCheck}
          >
            {checking ? "Checking..." : "Check again →"}
          </button>
          {links.map((link) => (
            <a
              key={link.label}
              className="mm-quiet-link"
              href={link.href ?? undefined}
              target="_blank"
              rel="noreferrer"
            >
              {link.label}
            </a>
          ))}
        </>
      }
    >
      {isWindows ? <UpdateReadyNotice /> : null}
      <p
        className="mm-status-line"
        title={status.summary}
        data-testid="suite-settings-release-status"
      >
        <Chip meaning={updateMeaning(status.status)}>
          {updateStatusLabel(status.status)}
        </Chip>
        <span>{version ?? status.summary}</span>
      </p>
      <LastKnownRelease status={status} />
      {showUpdateButton && status.windows_installer_url ? (
        <a
          href={status.windows_installer_url}
          target="_blank"
          rel="noreferrer"
          className={`${mmActionButtonClass({ variant: "primary" })} mm-sys-btn mt-3 inline-flex`}
        >
          Download the update
        </a>
      ) : null}
      {isWindows ? <UpdatePreferences /> : null}
      {dockerCommand ? <DockerUpgradeSteps command={dockerCommand} /> : null}
    </Panel>
  );
}

/** How a Docker install takes the update it has been told about: the command to run, and what to keep. */
function DockerUpgradeSteps({ command }: { command: string }) {
  return (
    <section
      className="mm-update-next"
      aria-labelledby="suite-settings-upgrade-docker-heading"
    >
      <div className="mm-update-next__head">
        <h4
          id="suite-settings-upgrade-docker-heading"
          className="mm-update-next__title"
        >
          What happens next
        </h4>
        <CopyCommandButton command={command} />
      </div>
      <p className="mm-update-command">{command}</p>
      <p className="mm-sys-note">
        Keep the same WEIR_HOME volume and WEIR_SESSION_SECRET value across
        upgrades so browser sessions and setup state continue cleanly.
      </p>
    </section>
  );
}

/** Which Weir is installed, whether a newer one exists, and how this install takes updates. */
export function UpdateSection() {
  const updateStatusQ = useUpdateStatusQuery();
  const status = updateStatusQ.data;
  const isWindows = status?.install_type === "windows";
  const updateSettingsQ = useUpdateSettingsQuery(isWindows);
  const notifyOnly = isWindows && updateSettingsQ.data?.mode === "NotifyOnly";
  const updateAvailable = status?.status === "update_available";

  if (updateStatusQ.isPending) {
    return (
      <Panel title="Updates" headingLevel={3} padded>
        <p className="mm-quiet-note">Checking for updates...</p>
      </Panel>
    );
  }
  if (!status) {
    return (
      <Panel title="Updates" headingLevel={3} padded>
        <p className="mm-status-text text-sm" data-status="broken" role="alert">
          {errorMessage(
            updateStatusQ.error,
            "Could not check for updates right now.",
          )}
        </p>
      </Panel>
    );
  }
  return (
    <ReleaseStatus
      status={status}
      checking={updateStatusQ.isFetching}
      isWindows={isWindows}
      dockerCommand={
        status.install_type === "docker" && updateAvailable
          ? (status.docker_update_command ?? null)
          : null
      }
      showUpdateButton={Boolean(notifyOnly && updateAvailable)}
      onCheck={() => void updateStatusQ.refetch()}
    />
  );
}
