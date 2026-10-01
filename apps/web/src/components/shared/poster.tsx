import { useState, type CSSProperties } from "react";

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
function initialsOf(title: string): string {
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

/**
 * A title's poster, 2:3, filling the box it is put in. Until the image has loaded, and wherever there is none
 * (no `url`, or the image fails), it is the title's initials on the colour of the workflow that has the file.
 */
export function Poster({
  url,
  title,
  workflow,
}: {
  /** Where Weir serves the poster, or nothing when the file has none. */
  url: string | null | undefined;
  title: string;
  workflow: string;
}) {
  const [failedUrl, setFailedUrl] = useState<string | null>(null);
  const style: CSSProperties = {
    ["--tile-hue" as string]: workflowHue(workflow),
  };
  return (
    <span className="mm-tile" style={style}>
      <span aria-hidden="true">{initialsOf(title)}</span>
      {url && url !== failedUrl ? (
        <img
          className="mm-tile__image"
          src={url}
          alt={title}
          loading="lazy"
          decoding="async"
          onError={() => setFailedUrl(url)}
        />
      ) : null}
    </span>
  );
}
