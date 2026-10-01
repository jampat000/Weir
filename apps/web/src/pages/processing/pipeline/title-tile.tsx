import type { CSSProperties } from "react";

/** The hue range of a workflow's tint: the full colour wheel. */
const HUE_DEGREES = 360;

/** A workflow's own hue, the same every time, so every file of a workflow wears the same colour. */
export function workflowHue(workflow: string): number {
  let hash = 0;
  for (const character of workflow) {
    hash = (hash * 31 + (character.codePointAt(0) ?? 0)) >>> 0;
  }
  return hash % HUE_DEGREES;
}

const IGNORED_WORDS = /^(the|a|an|of)$/i;
const EPISODE_CODE = /^S\d+E\d+$/i;

/** "The Quiet Harbour (2024)" reads "QH"; "Lioness S03E08" reads "L": the first letters of the first two words. */
export function initialsOf(title: string): string {
  const letters = title
    .replace(/\(\d{4}\)/g, " ")
    .split(/[^A-Za-z0-9]+/)
    .filter(
      (word) => word && !IGNORED_WORDS.test(word) && !EPISODE_CODE.test(word),
    )
    .slice(0, 2)
    .map((word) => word[0].toUpperCase())
    .join("");
  return letters || title.slice(0, 1).toUpperCase();
}

/** The style that tints a tile for a workflow. */
export function tileStyle(workflow: string): CSSProperties {
  return { ["--tile-hue" as string]: workflowHue(workflow) };
}

/**
 * The 2:3 tile that stands where a poster would: Weir has none, so it is the title's initials on the
 * colour of the workflow that has the file. It fills the box it is put in.
 */
export function TitleTile({
  title,
  workflow,
}: {
  title: string;
  workflow: string;
}) {
  return (
    <span aria-hidden="true" className="mm-tile" style={tileStyle(workflow)}>
      {initialsOf(title)}
    </span>
  );
}
