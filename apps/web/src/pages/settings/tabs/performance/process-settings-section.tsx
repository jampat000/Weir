import { useEffect, useId, useState } from "react";
import { PageLoading } from "../../../../components/shared/page-loading";
import {
  NumberWithUnit,
  SettingRow,
  SettingsGroup,
} from "../../../../components/shared/settings-group";
import { MmOnOffSwitch } from "../../../../components/ui/mm-on-off-switch";
import { canEdit } from "../../../../lib/auth/can-edit";
import { useMeQuery } from "../../../../lib/auth/queries";
import {
  useProcessingFilesAtOnceQuery,
  useProcessingOperatorSettingsQuery,
  useProcessingOperatorSettingsSaveMutation,
} from "../../../../lib/processing/queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { errorMessage } from "../../../../lib/api/error-message";
import { SaveModelNote } from "../../save-model-note";
import { SettingsLoadError } from "../../settings-load-error";

/** Files at once goes from one to ten (#633). */
const FILES_AT_ONCE = Array.from({ length: 10 }, (_, i) => i + 1);

/** 1024 MB to a GB, for the free-space setting a person reads and types in GB. */
const MB_PER_GB = 1024;
/** Free-space GB never needs more precision than this to read sensibly. */
const FREE_SPACE_GB_DECIMALS = 2;

/** The minimum free disk space, in GB, rounded so it never reads as a repeating decimal. */
function formatFreeSpaceGb(minimumFreeDiskSpaceMb: number): string {
  const gb = Math.max(0, minimumFreeDiskSpaceMb / MB_PER_GB).toFixed(
    FREE_SPACE_GB_DECIMALS,
  );
  return gb.replace(/\.?0+$/, "");
}

/**
 * Settings › Performance: how hard Weir works, what it checks before it starts a file, and what it keeps when
 * one fails. The cleanup switches are on Cleanup, each beside its own timer.
 */
export function ProcessSettingsSection() {
  const me = useMeQuery();
  const q = useProcessingOperatorSettingsQuery();
  const save = useProcessingOperatorSettingsSaveMutation();
  const filesAtOnce = useProcessingFilesAtOnceQuery();
  const ids = useId();
  const editable = canEdit(me.data?.role);

  const [maxConcurrentFiles, setMaxConcurrentFiles] = useState(1);
  const [runnerCapacity, setRunnerCapacity] = useState("4");
  const [runnerCostSd, setRunnerCostSd] = useState("1");
  const [runnerCost720p, setRunnerCost720p] = useState("1");
  const [runnerCost1080p, setRunnerCost1080p] = useState("2");
  const [runnerCost4k, setRunnerCost4k] = useState("4");
  const [runnerCostUndetermined, setRunnerCostUndetermined] = useState("0");
  const [runnerBudgetEnabled, setRunnerBudgetEnabled] = useState(false);
  const [minFileAgeSeconds, setMinFileAgeSeconds] = useState("60");
  const [minInputFileSizeMb, setMinInputFileSizeMb] = useState("50");
  const [minimumFreeDiskSpaceGb, setMinimumFreeDiskSpaceGb] = useState("5");
  const [keepFailedWorkFiles, setKeepFailedWorkFiles] = useState(false);

  useEffect(() => {
    if (!q.data) return;
    setMaxConcurrentFiles(q.data.max_concurrent_files);
    setRunnerCapacity(String(q.data.runner_capacity));
    setRunnerCostSd(String(q.data.runner_cost_sd));
    setRunnerCost720p(String(q.data.runner_cost_720p));
    setRunnerCost1080p(String(q.data.runner_cost_1080p));
    setRunnerCost4k(String(q.data.runner_cost_4k));
    setRunnerCostUndetermined(String(q.data.runner_cost_undetermined));
    setRunnerBudgetEnabled(q.data.runner_budget_enabled);
    setMinFileAgeSeconds(String(q.data.min_file_age_seconds));
    setMinInputFileSizeMb(String(q.data.min_input_file_size_mb));
    setMinimumFreeDiskSpaceGb(
      formatFreeSpaceGb(q.data.minimum_free_disk_space_mb),
    );
    setKeepFailedWorkFiles(q.data.keep_failed_work_files);
  }, [q.data]);

  if (q.isPending || me.isPending) {
    return <PageLoading label="Loading performance settings" />;
  }
  if (q.isError) {
    return <SettingsLoadError what="performance settings" />;
  }
  if (!q.data) return null;

  const int = (raw: string) => Number.parseInt(raw, 10);
  const costs = [
    int(runnerCapacity),
    int(runnerCostSd),
    int(runnerCost720p),
    int(runnerCost1080p),
    int(runnerCost4k),
    int(runnerCostUndetermined),
  ];
  const minimumFreeMb = Math.round(
    Number.parseFloat(minimumFreeDiskSpaceGb) * 1024,
  );
  const valid =
    maxConcurrentFiles >= 1 &&
    maxConcurrentFiles <= FILES_AT_ONCE.length &&
    costs.every(
      (value, index) =>
        Number.isFinite(value) && value >= (index === 0 ? 1 : 0) && value <= 64,
    ) &&
    int(minFileAgeSeconds) >= 0 &&
    int(minInputFileSizeMb) >= 0 &&
    Number.isFinite(minimumFreeMb) &&
    minimumFreeMb >= 0;
  const dirty =
    maxConcurrentFiles !== q.data.max_concurrent_files ||
    runnerCapacity !== String(q.data.runner_capacity) ||
    runnerCostSd !== String(q.data.runner_cost_sd) ||
    runnerCost720p !== String(q.data.runner_cost_720p) ||
    runnerCost1080p !== String(q.data.runner_cost_1080p) ||
    runnerCost4k !== String(q.data.runner_cost_4k) ||
    runnerCostUndetermined !== String(q.data.runner_cost_undetermined) ||
    runnerBudgetEnabled !== q.data.runner_budget_enabled ||
    minFileAgeSeconds !== String(q.data.min_file_age_seconds) ||
    minInputFileSizeMb !== String(q.data.min_input_file_size_mb) ||
    minimumFreeDiskSpaceGb !==
      formatFreeSpaceGb(q.data.minimum_free_disk_space_mb) ||
    keepFailedWorkFiles !== q.data.keep_failed_work_files;
  const locked = !editable || save.isPending;

  const cost = (label: string, value: string, set: (v: string) => void) => (
    <SettingRow label={label} htmlFor={`${ids}-${label}`}>
      <NumberWithUnit
        id={`${ids}-${label}`}
        value={value}
        unit="units"
        min={0}
        max={64}
        disabled={locked}
        onChange={set}
      />
    </SettingRow>
  );

  return (
    <div data-testid="processing-process-settings">
      <SaveModelNote model="explicit" />
      <p className="mm-quiet-note">
        How hard Weir works, what it checks before it starts a file, and what it
        keeps when one fails. The defaults suit most machines.
      </p>
      <div className="mm-setgroups mm-setgroups--columns">
        <div>
          <SettingsGroup
            title="How much at once"
            detail="More at once finishes a queue sooner but works the disks harder. Past two or three, a slow disk gains little."
          >
            <SettingRow
              label="Files at once"
              hint={
                filesAtOnce.data ? (
                  <span data-testid="processing-files-at-once-readout">
                    {filesAtOnce.data.running} running now.{" "}
                    {filesAtOnce.data.message || "Nothing is waiting."}
                  </span>
                ) : undefined
              }
              stacked
            >
              <div
                className="mm-choice-row"
                role="group"
                aria-label="Files at once"
              >
                {FILES_AT_ONCE.map((n) => (
                  <button
                    key={n}
                    type="button"
                    aria-pressed={n === maxConcurrentFiles}
                    disabled={locked}
                    onClick={() => setMaxConcurrentFiles(n)}
                  >
                    {n}
                  </button>
                ))}
              </div>
            </SettingRow>
            <SettingRow
              label="Also weigh files by resolution"
              hint="For a machine that copes with several small files but only one 4K file. Off, the number above means exactly that."
            >
              <MmOnOffSwitch
                id={`${ids}-budget`}
                label="Also weigh files by resolution"
                enabled={runnerBudgetEnabled}
                disabled={locked}
                onChange={setRunnerBudgetEnabled}
                layout="control"
              />
            </SettingRow>
            {runnerBudgetEnabled ? (
              <>
                <SettingRow
                  label="Budget"
                  hint="A file starts only while its cost fits in what is left."
                  htmlFor={`${ids}-capacity`}
                >
                  <NumberWithUnit
                    id={`${ids}-capacity`}
                    value={runnerCapacity}
                    unit="units"
                    min={1}
                    max={64}
                    disabled={locked}
                    onChange={setRunnerCapacity}
                  />
                </SettingRow>
                {cost("An SD file costs", runnerCostSd, setRunnerCostSd)}
                {cost("A 720p file costs", runnerCost720p, setRunnerCost720p)}
                {cost(
                  "A 1080p file costs",
                  runnerCost1080p,
                  setRunnerCost1080p,
                )}
                {cost("A 4K file costs", runnerCost4k, setRunnerCost4k)}
                {cost(
                  "A file of unknown size costs",
                  runnerCostUndetermined,
                  setRunnerCostUndetermined,
                )}
              </>
            ) : null}
          </SettingsGroup>

          <SettingsGroup
            title="Before a file starts"
            detail="Checks that stop Weir touching a file that is not ready. Keep your downloader's own limits too."
          >
            <SettingRow
              label="Wait until it stops changing for"
              htmlFor={`${ids}-age`}
            >
              <NumberWithUnit
                id={`${ids}-age`}
                value={minFileAgeSeconds}
                unit="seconds"
                min={0}
                disabled={locked}
                onChange={setMinFileAgeSeconds}
              />
            </SettingRow>
            <SettingRow
              label="Skip files smaller than"
              hint="Samples and extras."
              htmlFor={`${ids}-size`}
            >
              <NumberWithUnit
                id={`${ids}-size`}
                value={minInputFileSizeMb}
                unit="MB"
                min={0}
                disabled={locked}
                onChange={setMinInputFileSizeMb}
              />
            </SettingRow>
            <SettingRow
              label="Keep free on the output drive"
              hint="No new file starts below this."
              htmlFor={`${ids}-free`}
            >
              <NumberWithUnit
                id={`${ids}-free`}
                value={minimumFreeDiskSpaceGb}
                unit="GB"
                min={0}
                step={0.1}
                disabled={locked}
                onChange={setMinimumFreeDiskSpaceGb}
              />
            </SettingRow>
          </SettingsGroup>
        </div>

        <div>
          <SettingsGroup
            title="When a file fails"
            detail="What Weir leaves behind for you to look at. Deleting the download of a failed file is a Cleanup job."
          >
            <SettingRow
              label="Keep the half-written copy"
              hint="Left in the work folder so you can look at it. Cleanup's leftover-work-file sweep removes it once it is a day old."
            >
              <MmOnOffSwitch
                id={`${ids}-keep-failed`}
                label="Keep the half-written copy"
                enabled={keepFailedWorkFiles}
                disabled={locked}
                onChange={setKeepFailedWorkFiles}
                layout="control"
              />
            </SettingRow>
          </SettingsGroup>
        </div>
      </div>

      {save.isError ? (
        <p className="mm-status-text--failed mt-3 text-sm" role="alert">
          {errorMessage(save.error, "Save failed.")}
        </p>
      ) : null}
      <div className="mt-4 flex justify-end">
        <button
          type="button"
          className={mmActionButtonClass({ variant: "primary" })}
          disabled={!editable || !dirty || !valid || save.isPending}
          onClick={() =>
            save.mutate({
              max_concurrent_files: maxConcurrentFiles,
              runner_capacity: costs[0],
              runner_cost_sd: costs[1],
              runner_cost_720p: costs[2],
              runner_cost_1080p: costs[3],
              runner_cost_4k: costs[4],
              runner_cost_undetermined: costs[5],
              runner_budget_enabled: runnerBudgetEnabled,
              keep_failed_work_files: keepFailedWorkFiles,
              min_file_age_seconds: int(minFileAgeSeconds),
              min_input_file_size_mb: int(minInputFileSizeMb),
              minimum_free_disk_space_mb: minimumFreeMb,
            })
          }
        >
          {save.isPending
            ? "Saving…"
            : dirty
              ? "Save performance settings"
              : "No changes to save"}
        </button>
      </div>
    </div>
  );
}
