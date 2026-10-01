/**
 * The folder chain behind a workflow: does each tool in the line agree about where files are? Weir reads its own
 * folders, then asks each linked manager and each download client, and each answers in the lines the real server
 * words: read for itself, a problem with its fix, or only taken on someone's word.
 */
import { unreachableText } from "../engine/connection-health.mjs";
import { importRootOf, managerLabel } from "../fixtures/connections.mjs";
import { downloadClientKindsOf } from "../fixtures/workflows.mjs";
import { shaped } from "../openapi/skeleton.mjs";

const LOOPBACK_HOST = "localhost";
/** The download client a manager keeps downloads in when it is not one of the clients Weir is connected to. */
const UNCONNECTED_CLIENT = "Transmission";
const ARR_CLIENT_NAMES = { radarr: "qBittorrent", sonarr: "SABnzbd" };

const line = (state, text) => ({ state, text });
const ok = (text) => line("ok", text);

const isUp = (connection) => connection.last_test_ok !== false;

/** @param {Record<string, any>} library */
function localLines(library) {
  const work = `D:\\Weir\\work\\${library.name ?? "new workflow"}`;
  return [
    ok(`Weir can read the watched folder ${library.watched_folder}.`),
    ok(`Weir can read the work folder ${work}.`),
    ok(`Weir can read and write the output folder ${library.output_folder}.`),
    ok(
      "The work folder and output folder are on the same drive, so a finished file is moved into place instantly.",
    ),
  ];
}

/**
 * Which download client a manager says it keeps downloads in, and whether it says where that client saves.
 * @param {Record<string, any>} manager
 * @param {import("../store.mjs").Store} store
 */
function downloadClientOf(manager, store) {
  const silent = store.managersSilentAboutDownloads.has(manager.id);
  return {
    name: silent
      ? UNCONNECTED_CLIENT
      : (ARR_CLIENT_NAMES[manager.kind] ?? "its download client"),
    saysWhereItSaves: !silent,
  };
}

/** What a Sonarr or Radarr reads as when it answers. */
function answeringLines(manager, library, store) {
  const label = managerLabel(manager);
  const client = downloadClientOf(manager, store);
  const clientLine = client.saysWhereItSaves
    ? ok(
        `${client.name} saves ${label}'s downloads to ${library.watched_folder}, inside Weir's watched folder.`,
      )
    : line(
        "unverified",
        `${label} does not say where ${client.name} saves its downloads. Weir cannot verify they land in ${library.watched_folder} or inside it. Connect ${client.name} to Weir under Settings → Media managers to check it.`,
      );
  return [
    ok(`Completed Download Handling is on in ${label}.`),
    ok(
      `${label} maps ${library.watched_folder} to ${library.output_folder} for "${LOOPBACK_HOST}", so it looks for these downloads in Weir's output folder.`,
    ),
    clientLine,
  ];
}

/**
 * One media manager's link in a workflow's folder chain, in the shape of a setup check.
 * @param {Record<string, any>} manager
 * @param {Record<string, any>} library
 * @param {import("../store.mjs").Store} store
 */
export function managerLink(manager, library, store) {
  const answering = isUp(manager);
  const lines = answering
    ? answeringLines(manager, library, store)
    : [
        line(
          "problem",
          unreachableText(managerLabel(manager), manager.base_url),
        ),
      ];
  return {
    connection_id: manager.id,
    kind: manager.kind,
    name: manager.name,
    label: managerLabel(manager),
    flow: "remote_path_mapping",
    ready: answering,
    lines,
    mapping: answering
      ? {
          hosts: [LOOPBACK_HOST],
          remote_path: library.watched_folder,
          local_path: library.output_folder,
        }
      : null,
    story: {
      source_category: library.media_type === "tv" ? "tv" : "movies",
      manager_library: null,
      root_folder: importRootOf(manager),
    },
    suggested_watched_folder: library.watched_folder,
    suggested_output_folder: null,
  };
}

function downloadClientLink(client, library) {
  const lines = isUp(client)
    ? [
        ok(
          `${client.name}'s default completed-downloads folder is ${library.watched_folder}, inside this workflow's watched folder.`,
        ),
      ]
    : [
        line(
          "unverified",
          `${client.name} did not say where it saves, so Weir cannot verify that downloads land in ${library.watched_folder}. Check it is running and its saved address and login are right.`,
        ),
      ];
  return {
    connection_id: client.id,
    kind: client.kind,
    name: client.name,
    label: client.name,
    ready: true,
    lines,
  };
}

/**
 * @param {Record<string, any>} library
 * @param {import("../store.mjs").Store} store
 */
export function folderChainOf(library, store) {
  const clientKinds = downloadClientKindsOf(library);
  const managers = store.managers
    .filter(
      (manager) =>
        manager.enabled && library.manager_connection_ids?.includes(manager.id),
    )
    .map((manager) => managerLink(manager, library, store));
  return shaped("LibraryFolderChainOut", {
    library_id: library.id,
    ready: managers.every((manager) => manager.ready),
    local: { ready: true, lines: localLines(library) },
    managers,
    download_clients: store.downloadClients
      .filter((client) => client.enabled && clientKinds.includes(client.kind))
      .map((client) => downloadClientLink(client, library)),
  });
}
