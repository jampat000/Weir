// @vitest-environment node
import { describe, expect, it } from "vitest";

import { Artwork, POSTER_PATH } from "./artwork.mjs";

const SINTEL = {
  id: "movie-sintel-2010",
  mediaType: "movie",
  title: "Sintel",
  year: 2010,
};

function artwork() {
  let on = true;
  const instance = new Artwork({ titles: [SINTEL], isEnabled: () => on });
  return {
    instance,
    switchOff: () => {
      on = false;
    },
  };
}

describe("the simulation's artwork", () => {
  it("gives a known title the address its poster is served at", () => {
    expect(artwork().instance.urlFor(SINTEL.id)).toBe(
      `${POSTER_PATH}/${SINTEL.id}`,
    );
  });

  it("gives no address for a file with no poster id or an id nobody knows", () => {
    const { instance } = artwork();

    expect(instance.urlFor(null)).toBeNull();
    expect(instance.urlFor("movie-nothing-1999")).toBeNull();
  });

  it("gives no address once artwork is switched off in Settings", () => {
    const { instance, switchOff } = artwork();
    switchOff();

    expect(instance.urlFor(SINTEL.id)).toBeNull();
  });

  it("draws a poster for a title whose real one has not arrived", () => {
    const image = artwork().instance.image(SINTEL.id);

    expect(image?.contentType).toBe("image/svg+xml");
    expect(image?.body.toString()).toContain("Sintel");
  });

  it("serves the real poster once it has arrived", () => {
    const { instance } = artwork();
    const real = { contentType: "image/jpeg", body: Buffer.from([1, 2, 3]) };

    instance.keep(SINTEL.id, real);

    expect(instance.image(SINTEL.id)).toBe(real);
  });

  it("has no picture for an id nobody knows", () => {
    expect(artwork().instance.image("movie-nothing-1999")).toBeNull();
  });
});
