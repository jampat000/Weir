/** The folder chain behind a workflow: does each tool in the line agree about where files are? Always in sync here. */
import { shaped } from "../openapi/skeleton.mjs";

const ok = (text) => ({ state: "ok", text });

export function managerLink(manager, library) {
  return {
    connection_id: manager.id,
    kind: manager.kind,
    name: manager.name,
    label:
      manager.kind === "radarr"
        ? "Radarr"
        : manager.kind === "sonarr"
          ? "Sonarr"
          : manager.kind,
    flow: "handoff",
    ready: true,
    lines: [
      ok(`${manager.name} is told when Weir hands a cleaned copy back.`),
      ok(`It imports from ${library.output_folder}.`),
    ],
    mapping: null,
    suggested_watched_folder: library.watched_folder,
    suggested_output_folder: library.output_folder,
  };
}

function downloadClientLink(client, library) {
  return {
    connection_id: client.id,
    kind: client.kind,
    name: client.name,
    label: client.name,
    ready: true,
    lines: [
      ok(
        `${client.name} saves finished downloads in ${library.watched_folder}.`,
      ),
    ],
  };
}

/**
 * @param {Record<string, any>} library
 * @param {import("../store.mjs").Store} store
 */
export function folderChainOf(library, store) {
  const usesClient = (client) =>
    client.enabled &&
    (library.media_type === "tv"
      ? client.kind === "sabnzbd"
      : client.kind === "qbittorrent");
  return shaped("LibraryFolderChainOut", {
    library_id: library.id,
    ready: true,
    local: {
      ready: true,
      lines: [
        ok(`Weir can read ${library.watched_folder}.`),
        ok(`Weir can write to ${library.output_folder}.`),
      ],
    },
    managers: store.managers
      .filter((manager) => library.manager_connection_ids?.includes(manager.id))
      .map((manager) => managerLink(manager, library)),
    download_clients: store.downloadClients
      .filter(usesClient)
      .map((client) => downloadClientLink(client, library)),
  });
}
