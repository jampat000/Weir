/**
 * Everything a person can change from Settings, held in memory for the length of the run. A save writes here and is
 * read back by the next request, so the screens behave as they do against a real server; nothing touches a disk.
 */
import {
  initialDownloadClients,
  initialManagers,
} from "./fixtures/connections.mjs";
import {
  initialDirectPlayDevices,
  initialMetadataProvider,
  initialOperatorSettings,
  initialSuiteSettings,
  initialUpdateSettings,
} from "./fixtures/settings.mjs";
import { initialLibraries, initialRuleSets } from "./fixtures/workflows.mjs";
import { shaped } from "./openapi/skeleton.mjs";
import { toWire, HOUR_MS } from "./wire-time.mjs";

export const SIGNED_IN_USER = { id: 1, username: "admin", role: "admin" };
const BACKUP_COUNT = 3;
const BACKUP_BYTES = 38_400;

function initialBackups() {
  return Array.from({ length: BACKUP_COUNT }, (_, index) =>
    shaped("SuiteConfigurationBackupItemOut", {
      id: index + 1,
      file_name: `weir-config-${index + 1}.zip`,
      size_bytes: BACKUP_BYTES + index * 512,
      created_at: toWire(Date.now() - (index * 24 + 9) * HOUR_MS),
    }),
  );
}

export function createStore() {
  return {
    libraries: initialLibraries(),
    ruleSets: initialRuleSets(),
    managers: initialManagers(),
    downloadClients: initialDownloadClients(),
    /** Each workflow's library-cleaning settings, once someone has opened or changed them. */
    libraryModes: /** @type {Record<number, Record<string, any>>} */ ({}),
    notificationChannels: /** @type {Record<string, any>[]} */ ([]),
    backups: initialBackups(),
    suite: initialSuiteSettings(),
    operator: initialOperatorSettings(),
    updateSettings: initialUpdateSettings(),
    metadataProvider: initialMetadataProvider(),
    directPlay: initialDirectPlayDevices(),
    session: {
      signedIn: true,
      theme: /** @type {"light" | "dark" | null} */ (null),
    },
  };
}

/** @typedef {ReturnType<typeof createStore>} Store */

/** The next unused numeric id in a list of records. @param {{ id: number }[]} records */
export function nextId(records) {
  return (
    records.reduce((highest, record) => Math.max(highest, record.id), 0) + 1
  );
}
