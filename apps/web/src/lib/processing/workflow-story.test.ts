import { describe, expect, it } from "vitest";

import { workflowStory } from "./workflow-story";

const PATH = {
  watched: "C:\\Downloads\\Completed\\Movies",
  work: "C:\\Temp\\Movies",
  output: "E:\\Weir\\Ready\\Movies",
};

describe("a workflow's story", () => {
  it("tells a Weir only workflow as the folders it watches, works in and cleans into", () => {
    expect(workflowStory(PATH, null, null)).toBe(
      "Watches C:\\Downloads\\Completed\\Movies → works in C:\\Temp\\Movies → cleaned into E:\\Weir\\Ready\\Movies.",
    );
  });

  it("names Weir's own work area when no work folder is set", () => {
    expect(workflowStory({ ...PATH, work: "" }, null, null)).toContain(
      "works in its own work area",
    );
  });

  it("uses Deluno's own words for its download client category and the library it imports into", () => {
    const story = workflowStory(
      PATH,
      { id: 1, name: "Deluno", kind: "deluno" },
      { category: "movies", managerLibrary: "Movies", rootFolder: null },
    );

    expect(story).toBe(
      "Comes from Deluno's download client, category movies (C:\\Downloads\\Completed\\Movies) → Weir works in C:\\Temp\\Movies → cleaned into E:\\Weir\\Ready\\Movies → Deluno imports it into its library Movies.",
    );
  });

  it("uses a root folder for Radarr and Sonarr", () => {
    const story = workflowStory(
      PATH,
      { id: 2, name: "Radarr", kind: "radarr" },
      { category: "radarr", managerLibrary: null, rootFolder: "Z:\\Movies" },
    );

    expect(story).toContain("category radarr");
    expect(
      story.endsWith("Radarr imports it into root folder Z:\\Movies."),
    ).toBe(true);
  });

  it("leaves out a name the manager did not report instead of guessing", () => {
    const story = workflowStory(
      PATH,
      { id: 1, name: "Deluno", kind: "deluno" },
      null,
    );

    expect(story).toBe(
      "Comes from Deluno's download client (C:\\Downloads\\Completed\\Movies) → Weir works in C:\\Temp\\Movies → cleaned into E:\\Weir\\Ready\\Movies → Deluno imports it.",
    );
  });

  it("does not offer a root folder for Deluno or a library for Radarr", () => {
    const deluno = workflowStory(
      PATH,
      { id: 1, name: "Deluno", kind: "deluno" },
      { category: null, managerLibrary: null, rootFolder: "Z:\\Movies" },
    );
    const radarr = workflowStory(
      PATH,
      { id: 2, name: "Radarr", kind: "radarr" },
      { category: null, managerLibrary: "Movies", rootFolder: null },
    );

    expect(deluno).not.toContain("Z:\\Movies");
    expect(radarr).not.toContain("library Movies");
  });
});
