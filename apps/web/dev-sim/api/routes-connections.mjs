/** The media managers and download clients Weir is linked to, and the setup checks that read them. */
import { CONNECTION_KIND } from "../engine/connection-health.mjs";
import {
  importRootOf,
  managerCapabilities,
  managerDefaults,
} from "../fixtures/connections.mjs";
import { knownFields } from "../openapi/fields.mjs";
import { shaped } from "../openapi/skeleton.mjs";
import { registerCollection } from "./collection.mjs";
import { folderChainOf, managerLink } from "./folder-chain.mjs";
import { notFound } from "./reply.mjs";

/** A person's test of a connection: it answers or not as it does in the scenario, and the record remembers when. */
const tested =
  (kind) =>
  (connection, { sim }) =>
    sim.engine.connections.test(kind, connection, sim.now());

function withConnection(records, handler) {
  return (context) => {
    const connection = records(context.sim).find(
      (candidate) => candidate.id === Number(context.params.id),
    );
    return connection
      ? handler(connection, context)
      : notFound("That connection does not exist.");
  };
}

function proposedChain(store, mediaType, folders) {
  const library = {
    id: 0,
    media_type: mediaType,
    watched_folder: folders.watched,
    output_folder: folders.output,
    manager_connection_ids: store.managers
      .filter(
        (manager) =>
          manager.kind === (mediaType === "tv" ? "sonarr" : "radarr"),
      )
      .map((manager) => manager.id),
  };
  return { problem: null, chain: folderChainOf(library, store) };
}

function librarySuggestions(store) {
  return shaped("LibrarySuggestionsOut", {
    libraries: store.libraries.map((library) => ({
      library_id: library.id,
      name: library.name,
      media_type: library.media_type,
      watched_folder: library.watched_folder,
      output_folder: library.output_folder,
      source_label: `${library.name} workflow`,
      manager_connection_ids: library.manager_connection_ids ?? [],
    })),
    notes: [],
  });
}

function discoverable(manager, store) {
  const mediaType = manager.kind === "radarr" ? "movie" : "tv";
  const root = importRootOf(manager);
  const library = store.libraries.find((candidate) =>
    candidate.manager_connection_ids?.includes(manager.id),
  );
  return [
    shaped("DiscoverableLibraryOut", {
      key: `${manager.id}:${mediaType}`,
      name: root.split("\\").pop(),
      already_imported: library !== undefined,
      media_type: mediaType,
      root_path: root,
      output_path: library?.output_folder ?? null,
      processes_before_import: true,
    }),
  ];
}

/** @param {import("./router.mjs").Router} router */
export function registerConnectionRoutes(router) {
  const managers = (sim) => sim.store.managers;
  const clients = (sim) => sim.store.downloadClients;
  const managerPath = "/api/v1/media-managers/connections";
  const clientPath = "/api/v1/download-clients/connections";

  router.get("/api/v1/media-managers/capabilities", ({ sim }) =>
    managerCapabilities(sim.store.managers),
  );
  router.post(
    `${managerPath}/:id/test`,
    withConnection(managers, tested(CONNECTION_KIND.MANAGER)),
  );
  router.post(
    `${managerPath}/:id/webhook-secret`,
    withConnection(managers, (manager) => ({
      connection_id: manager.id,
      header_name: "X-Webhook-Secret",
      webhook_secret: "dev-sim-webhook-secret",
      webhook_url_path: manager.webhook_url_path,
    })),
  );
  router.put(
    `${managerPath}/:id/lanes/:lane`,
    withConnection(managers, (manager, { params, body }) => {
      const lane = manager.lanes?.find(
        (candidate) => candidate.lane === params.lane,
      );
      return lane
        ? Object.assign(lane, knownFields("MediaManagerSearchLaneOut", body))
        : notFound("That lane does not exist.");
    }),
  );
  registerCollection(router, {
    path: managerPath,
    schemaName: "MediaManagerConnectionOut",
    idParam: "id",
    records: managers,
    defaults: managerDefaults,
    onCreate: (record) => ({
      webhook_url_path: `/api/v1/intake/webhook/${record.kind}-${record.id}`,
      webhook_secret_is_set: true,
    }),
  });

  router.post(
    `${clientPath}/:id/test`,
    withConnection(clients, tested(CONNECTION_KIND.CLIENT)),
  );
  router.get("/api/v1/download-clients/suggestions", ({ sim, query }) =>
    sim.store.downloadClients.map((client) =>
      shaped("DownloadClientSuggestionOut", {
        connection_id: client.id,
        kind: client.kind,
        name: client.name,
        label: client.name,
        suggested_watched_folder:
          query.get("media_type") === "tv"
            ? "D:\\Downloads\\TV"
            : "D:\\Downloads\\Movies",
        category_folders: [
          {
            category: query.get("media_type") ?? "movie",
            folder:
              query.get("media_type") === "tv"
                ? "D:\\Downloads\\TV"
                : "D:\\Downloads\\Movies",
          },
        ],
      }),
    ),
  );
  registerCollection(router, {
    path: clientPath,
    schemaName: "DownloadClientConnectionOut",
    idParam: "id",
    records: clients,
  });

  router.get("/api/v1/processing/library-suggestions", ({ sim }) =>
    librarySuggestions(sim.store),
  );
  router.get("/api/v1/processing/manager-setup", ({ sim, query }) => {
    const mediaType = query.get("media_type") ?? "movie";
    const library = {
      media_type: mediaType,
      watched_folder: query.get("watched_folder") ?? "",
      output_folder: query.get("output_folder") ?? "",
    };
    const kind = mediaType === "tv" ? "sonarr" : "radarr";
    return {
      media_type: mediaType,
      managers: sim.store.managers
        .filter((manager) => manager.kind === kind)
        .map((manager) => managerLink(manager, library, sim.store)),
    };
  });
  router.post("/api/v1/processing/library-check", ({ sim, body }) => ({
    movie: body.movie_watched_folder
      ? proposedChain(sim.store, "movie", {
          watched: body.movie_watched_folder,
          output: body.movie_output_folder ?? "",
        })
      : null,
    tv: body.tv_watched_folder
      ? proposedChain(sim.store, "tv", {
          watched: body.tv_watched_folder,
          output: body.tv_output_folder ?? "",
        })
      : null,
  }));
  router.get(
    "/api/v1/processing/libraries/discover/:id",
    withConnection(managers, (manager, { sim }) =>
      discoverable(manager, sim.store),
    ),
  );
  router.get("/api/v1/processing/libraries/discover/:id/drift", () => []);
}
