import { QuietSection } from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import { useUpdateStatusQuery } from "../../../../lib/settings/queries";
import type { UpdateStatus } from "../../../../lib/settings/types";
import { mmStatusPillClass } from "../../../../lib/ui/mm-status-tone";
import { UpdateModeSection } from "./update-mode-section";
import { UpdateReadyNotice } from "./update-ready-notice";

/** "up to date" -> "Up to date": status pills across Weir are sentence case. */
function sentenceCase(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1);
}

function ReleaseStatus({
  status,
  checking,
  onCheck,
}: {
  status: UpdateStatus;
  checking: boolean;
  onCheck: () => void;
}) {
  // Coequal facts about this install, none of them a magnitude, so none of them is a hero.
  const facts = [
    { label: "Installed", value: status.current_version },
    { label: "Latest", value: status.latest_version || "Unknown" },
    { label: "Installed from", value: status.install_type },
  ];
  const links = [
    { label: "Download installer →", href: status.windows_installer_url },
    { label: "Release notes →", href: status.release_url },
  ].filter((link) => link.href);

  return (
    <QuietSection
      level={3}
      headingId="suite-settings-upgrade-heading"
      heading="Updates"
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
      <p
        className="mt-1 flex items-center gap-2 text-base font-semibold text-mm-text1"
        data-testid="suite-settings-release-status"
      >
        <span
          className={mmStatusPillClass(
            status.status === "update_available" ? "warning" : "healthy",
          )}
        >
          {sentenceCase(status.status.replaceAll("_", " "))}
        </span>
        {status.summary}
      </p>
      <dl className="mm-kv mt-2" aria-label="This Weir install">
        {facts.map((fact) => (
          <div key={fact.label}>
            <dt>{fact.label}</dt>
            <dd>{fact.value}</dd>
          </div>
        ))}
      </dl>
      {status.install_type === "windows" ? (
        <p className="mm-quiet-note mt-2">
          On Windows, the Weir tray app installs updates itself.
        </p>
      ) : null}
    </QuietSection>
  );
}

function DockerUpgradeSection({ command }: { command: string }) {
  return (
    <QuietSection
      level={3}
      headingId="suite-settings-upgrade-docker-heading"
      heading="What happens next"
    >
      <p className="mm-update-command">{command}</p>
      <p className="mm-quiet-note mt-2">
        Keep the same WEIR_HOME volume and WEIR_SESSION_SECRET value across
        upgrades so browser sessions and setup state continue cleanly.
      </p>
    </QuietSection>
  );
}

/** Which Weir is installed, whether a newer one exists, and how this install takes updates. */
export function UpdateSection() {
  const updateStatusQ = useUpdateStatusQuery();
  const status = updateStatusQ.data;
  const isWindows = status?.install_type === "windows";

  return (
    <div data-testid="suite-settings-upgrade-tab" className="mm-quiet-stack">
      <div className="mm-quiet-stack" data-testid="suite-settings-upgrade">
        {updateStatusQ.isPending ? (
          <p className="mm-quiet-note">Checking for updates...</p>
        ) : !status ? (
          <p className="text-sm text-mm-status-failed-text" role="alert">
            {errorMessage(
              updateStatusQ.error,
              "Could not check for updates right now.",
            )}
          </p>
        ) : (
          <>
            {isWindows ? <UpdateReadyNotice /> : null}
            <ReleaseStatus
              status={status}
              checking={updateStatusQ.isFetching}
              onCheck={() => void updateStatusQ.refetch()}
            />
            {isWindows ? (
              <UpdateModeSection />
            ) : status.install_type === "docker" &&
              status.docker_update_command ? (
              <DockerUpgradeSection command={status.docker_update_command} />
            ) : null}
          </>
        )}
      </div>
    </div>
  );
}
