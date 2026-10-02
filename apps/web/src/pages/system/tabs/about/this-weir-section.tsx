import { useNavigate } from "react-router-dom";

import { Chip } from "../../../../components/panels/chip";
import { Panel } from "../../../../components/panels/panel";
import type { RunsAs } from "../../../../lib/system/system-stats-types";
import { useSystemOverviewQuery } from "../../../../lib/system/use-system-stats";
import { useUpdateStatusQuery } from "../../../../lib/settings/queries";
import type { AppSettings } from "../../../../lib/settings/types";
import { useSystemReadinessQuery } from "../../../../lib/system/readiness-queries";
import {
  sizeWords,
  splitAddress,
} from "../../../processing/dashboard/system/system-words";
import {
  installTypeLabel,
  updateStatusLabel,
  updateStatusTone,
} from "./update-words";

const SETUP_WIZARD_PATH = "/setup-wizard";

const RUNS_AS_LABELS: Readonly<Record<RunsAs, string>> = {
  service: "Service",
  app: "App",
  docker: "Docker",
};

const WIZARD_STATE_NOTES: Readonly<Record<string, string>> = {
  completed: "You finished the wizard.",
  skipped: "You skipped the wizard.",
};
const WIZARD_PENDING_NOTE = "The wizard has not been finished yet.";

/** "host:port", or the host alone when the address carries no port. */
function addressWords(address: string): string {
  const { host, port } = splitAddress(address);
  return port ? `${host}:${port}` : host;
}

/**
 * System › About: what this Weir is, as one short fact per line. They come from the environment, so none is
 * editable. The setup wizard to run again is the card's one action.
 */
export function ThisWeirSection({ settings }: { settings: AppSettings }) {
  const navigate = useNavigate();
  const machine = useSystemReadinessQuery().data;
  const update = useUpdateStatusQuery().data;
  const overview = useSystemOverviewQuery().data;
  const wizardState = (settings.setup_wizard_state || "pending")
    .trim()
    .toLowerCase();

  return (
    <Panel
      title="This Weir"
      headingId="about-this-weir-heading"
      padded
      dataTestId="suite-settings-global"
      aside={
        <button
          type="button"
          className="mm-quiet-link"
          title={WIZARD_STATE_NOTES[wizardState] ?? WIZARD_PENDING_NOTE}
          data-testid="suite-settings-open-setup-wizard"
          onClick={() => navigate(SETUP_WIZARD_PATH)}
        >
          Open setup wizard
        </button>
      }
    >
      <dl className="mm-kv mm-sys-facts">
        {machine ? (
          <div data-testid="about-machine-name">
            <dt>Name</dt>
            <dd title="Weir takes its name from the computer it runs on.">
              {machine.machine_name}
            </dd>
          </div>
        ) : null}
        <div>
          <dt>Version</dt>
          <dd>
            <span>
              {update?.current_version ?? machine?.version ?? "Checking…"}
            </span>
            {update ? (
              <Chip tone={updateStatusTone(update.status)}>
                {updateStatusLabel(update.status)}
              </Chip>
            ) : null}
          </dd>
        </div>
        {update ? (
          <div>
            <dt>Installed from</dt>
            <dd>{installTypeLabel(update.install_type)}</dd>
          </div>
        ) : null}
        {overview ? (
          <>
            <div>
              <dt>Data</dt>
              <dd title="Everything under Weir's home folder.">
                {sizeWords(overview.data_bytes)}
              </dd>
            </div>
            <div>
              <dt>Runs as</dt>
              <dd>{RUNS_AS_LABELS[overview.runs_as]}</dd>
            </div>
            <div>
              <dt>Address</dt>
              <dd>{addressWords(overview.address)}</dd>
            </div>
          </>
        ) : null}
      </dl>
      {machine?.machine_name_looks_generated ? (
        <p
          className="mm-status-text--warning mm-sys-note"
          data-testid="about-hostname-tip"
        >
          Set <code>hostname:</code> in your compose file so Weir shows your
          server&apos;s name.
        </p>
      ) : null}
    </Panel>
  );
}
