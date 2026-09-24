import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { errorMessage } from "../../../../lib/api/error-message";
import {
  fetchConfigurationBundle,
  fetchStoredConfigurationBackupBlob,
  postConfigurationBackupNow,
  putConfigurationBundle,
  type ConfigurationBundle,
} from "../../../../lib/settings/settings-api";
import { settingsKeys } from "../../../../lib/settings/query-keys";
import { saveBlobAs } from "../../../../lib/ui/save-file";

/** The longest file name a stored snapshot is saved under. */
const MAX_SNAPSHOT_NAME = 120;

/** Which part of the tab an action's result belongs next to. */
export type BackupActionSource = "export" | "snapshot";

/** Reads a configuration export from text, or says in plain words why it is not one. */
function parseBundle(
  text: string,
): { bundle: ConfigurationBundle } | { problem: string } {
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    return { problem: "This file is not valid JSON." };
  }
  if (typeof parsed !== "object" || parsed === null) {
    return { problem: "This file is not valid JSON." };
  }
  // Which format versions restore is the server's call; it answers 400 with a reason for one it cannot take.
  if (typeof (parsed as ConfigurationBundle).format_version !== "number") {
    return { problem: "This file is not a Weir configuration export." };
  }
  return { bundle: parsed as ConfigurationBundle };
}

/** Reads a chosen file as a configuration export, or says in plain words why it is not one. */
async function readBundle(
  file: File,
): Promise<{ bundle: ConfigurationBundle } | { problem: string }> {
  return parseBundle(await file.text());
}

/**
 * Downloading and restoring Weir's configuration. A restore is two steps: `chooseRestoreFile` (or
 * `chooseSavedBackup`) checks the file and holds it, and nothing is replaced until `confirmRestore`.
 *
 * Every result is tagged with the section it came from, so the caller can show it beside the buttons
 * that triggered it instead of in one shared banner for the whole tab.
 */
export function useBackupActions() {
  const queryClient = useQueryClient();
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [source, setSource] = useState<BackupActionSource | null>(null);
  const [pendingRestore, setPendingRestore] =
    useState<ConfigurationBundle | null>(null);
  const [pendingRestoreSource, setPendingRestoreSource] =
    useState<BackupActionSource>("export");

  const run = async (
    from: BackupActionSource,
    work: () => Promise<string>,
    failure: string,
  ) => {
    setSource(from);
    setProblem(null);
    setMessage(null);
    setBusy(true);
    try {
      setMessage(await work());
    } catch (error) {
      setProblem(errorMessage(error, failure));
    } finally {
      setBusy(false);
    }
  };

  /** The message or problem to show beside `from`'s own buttons, or nothing if the last result was elsewhere. */
  const resultFor = (from: BackupActionSource) =>
    source === from ? { message, problem } : { message: null, problem: null };

  return {
    busy,
    pendingRestore,
    resultFor,
    downloadConfiguration: () =>
      run(
        "export",
        async () => {
          const bundle = await fetchConfigurationBundle();
          const date = new Date().toISOString().slice(0, 10);
          saveBlobAs(
            new Blob([JSON.stringify(bundle, null, 2)], {
              type: "application/json",
            }),
            `weir-configuration-${date}.json`,
          );
          return "Download started.";
        },
        "Could not export.",
      ),
    backUpNow: () =>
      run(
        "export",
        async () => {
          await postConfigurationBackupNow();
          await queryClient.invalidateQueries({
            queryKey: settingsKeys.configurationBackups,
          });
          return "Backup created.";
        },
        "Could not create a backup.",
      ),
    downloadSnapshot: (id: number, fileName: string) =>
      run(
        "snapshot",
        async () => {
          const blob = await fetchStoredConfigurationBackupBlob(id);
          saveBlobAs(
            blob,
            fileName.replace(/[^\w.-]+/g, "_").slice(0, MAX_SNAPSHOT_NAME),
          );
          return "Download started.";
        },
        "Could not download snapshot.",
      ),
    chooseRestoreFile: async (file: File) => {
      setSource("export");
      setProblem(null);
      setMessage(null);
      const read = await readBundle(file);
      if ("problem" in read) {
        setProblem(read.problem);
      } else {
        setPendingRestoreSource("export");
        setPendingRestore(read.bundle);
      }
    },
    chooseSavedBackup: async (id: number) => {
      setSource("snapshot");
      setProblem(null);
      setMessage(null);
      setBusy(true);
      try {
        const blob = await fetchStoredConfigurationBackupBlob(id);
        const read = parseBundle(await blob.text());
        if ("problem" in read) {
          setProblem(read.problem);
        } else {
          setPendingRestoreSource("snapshot");
          setPendingRestore(read.bundle);
        }
      } catch (error) {
        setProblem(errorMessage(error, "Could not read this backup."));
      } finally {
        setBusy(false);
      }
    },
    cancelRestore: () => setPendingRestore(null),
    confirmRestore: () => {
      const bundle = pendingRestore;
      if (!bundle) return;
      setPendingRestore(null);
      void run(
        pendingRestoreSource,
        async () => {
          await putConfigurationBundle(bundle);
          // Everything read from the server may have changed, the settings drafts included.
          await queryClient.invalidateQueries();
          return "Configuration restored.";
        },
        "Could not restore.",
      );
    },
  };
}
