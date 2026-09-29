import type { WorkflowManager } from "./workflow-kind";

/** What a media manager reports about where a workflow's files come from and go to, in its own words. */
export type WorkflowSourceFacts = {
  /** The category the manager files this media type's downloads under in its download client. */
  category: string | null;
  /** The manager's own library that imports the cleaned file (Deluno). */
  managerLibrary: string | null;
  /** The manager's root folder the cleaned file is imported into (Radarr, Sonarr). */
  rootFolder: string | null;
};

export type WorkflowPath = {
  watched: string;
  work: string;
  output: string;
};

const ARROW = " → ";
const OWN_WORK_AREA = "its own work area";

/**
 * A workflow described as the path a file takes, ending in the words the linked media manager uses for where it
 * imports it. A name the manager did not report is left out rather than guessed, so the sentence is always
 * true, if shorter.
 */
export function workflowStory(
  path: WorkflowPath,
  manager: WorkflowManager | null,
  facts: WorkflowSourceFacts | null,
): string {
  const work = path.work || OWN_WORK_AREA;
  const watched = path.watched || "a folder to be chosen";
  const output = path.output || "an output folder to be chosen";
  if (manager === null) {
    return `Watches ${watched}${ARROW}works in ${work}${ARROW}cleaned into ${output}.`;
  }
  const category = facts?.category ? `, category ${facts.category}` : "";
  const imports = importsInto(manager, facts);
  return [
    `Comes from ${manager.name}'s download client${category} (${watched})`,
    `Weir works in ${work}`,
    `cleaned into ${output}`,
    `${manager.name} imports it${imports}.`,
  ].join(ARROW);
}

function importsInto(
  manager: WorkflowManager,
  facts: WorkflowSourceFacts | null,
): string {
  if (manager.kind === "deluno" && facts?.managerLibrary) {
    return ` into its library ${facts.managerLibrary}`;
  }
  if (
    (manager.kind === "radarr" || manager.kind === "sonarr") &&
    facts?.rootFolder
  ) {
    return ` into root folder ${facts.rootFolder}`;
  }
  return "";
}
