import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { errorMessage } from "../../../../lib/api/error-message";
import {
  fetchConfigurationBundle,
  fetchStoredConfigurationBackupBlob,
  putConfigurationBundle,
  type ConfigurationBundle,
} from "../../../../lib/settings/settings-api";
import { saveBlobAs } from "../../../../lib/ui/save-file";

/** The longest file name a stored snapshot is saved under. */
const MAX_SNAPSHOT_NAME = 120;

/** Reads a chosen file as a configuration export, or says in plain words why it is not one. */
async function readBundle(
  file: File,
): Promise<{ bundle: ConfigurationBundle } | { problem: string }> {
  let parsed: unknown;
  try {
    parsed = JSON.parse(await file.text());
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

/**
 * Downloading and restoring Weir's configuration. A restore is two steps: `chooseRestoreFile` checks
 * the file and holds it, and nothing is replaced until `confirmRestore`.
 */
export function useBackupActions() {
  const queryClient = useQueryClient();
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [pendingRestore, setPendingRestore] =
    useState<ConfigurationBundle | null>(null);

  const run = async (work: () => Promise<string>, failure: string) => {
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

  return {
    busy,
    message,
    problem,
    pendingRestore,
    setMessage,
    setProblem,
    downloadConfiguration: () =>
      run(async () => {
        const bundle = await fetchConfigurationBundle();
        const date = new Date().toISOString().slice(0, 10);
        saveBlobAs(
          new Blob([JSON.stringify(bundle, null, 2)], {
            type: "application/json",
          }),
          `weir-configuration-${date}.json`,
        );
        return "Download started.";
      }, "Could not export."),
    downloadSnapshot: (id: number, fileName: string) =>
      run(async () => {
        const blob = await fetchStoredConfigurationBackupBlob(id);
        saveBlobAs(
          blob,
          fileName.replace(/[^\w.-]+/g, "_").slice(0, MAX_SNAPSHOT_NAME),
        );
        return "Download started.";
      }, "Could not download snapshot."),
    chooseRestoreFile: async (file: File) => {
      setProblem(null);
      setMessage(null);
      const read = await readBundle(file);
      if ("problem" in read) setProblem(read.problem);
      else setPendingRestore(read.bundle);
    },
    cancelRestore: () => setPendingRestore(null),
    confirmRestore: () => {
      const bundle = pendingRestore;
      if (!bundle) return;
      setPendingRestore(null);
      void run(async () => {
        await putConfigurationBundle(bundle);
        // Everything read from the server may have changed, the settings drafts included.
        await queryClient.invalidateQueries();
        return "Configuration restored.";
      }, "Could not restore.");
    },
  };
}
