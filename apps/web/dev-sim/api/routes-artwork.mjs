/** The posters Weir serves from its own store, so the browser never asks a metadata service itself. */
import { notFound, Reply } from "./reply.mjs";

const NOT_FOUND = "Weir has no poster with that id.";

/** @param {import("./router.mjs").Router} router */
export function registerArtworkRoutes(router) {
  router.get("/api/v1/artwork/posters/:id", ({ sim, params }) => {
    const image = sim.artwork.image(params.id);
    return image
      ? new Reply(200, image.body, { contentType: image.contentType })
      : notFound(NOT_FOUND);
  });
}
