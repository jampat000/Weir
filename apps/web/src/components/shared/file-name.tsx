import { Fragment } from "react";

/** The last part of a path: the file's own name. */
export function baseName(path: string): string {
  return path.split(/[\\/]/).filter(Boolean).at(-1) ?? path;
}

/**
 * A file's full name, never cut short (James, 23 Sep 2026: "we need to ensure we see the file label"). A release name
 * is one long word to the browser, so it could only be clipped with an ellipsis or broken mid-word. It is offered a
 * line break after each dot, dash and underscore instead, so "The.Long.Tide.2024.2160p.WEB-DL.DDP5.1.HEVC.REPACK2"
 * wraps between its parts and the part that tells two releases apart is always there to read.
 */
export function FileName({
  path,
  className,
}: {
  path: string;
  className?: string;
}) {
  const name = baseName(path);
  // The extension stays with the part before it, so ".mkv" never sits on a line of its own.
  const extension = /\.[A-Za-z0-9]{2,4}$/.exec(name)?.[0] ?? "";
  const parts = name
    .slice(0, name.length - extension.length)
    .split(/(?<=[._-])/);
  parts[parts.length - 1] += extension;
  return (
    <span className={className} data-file-name>
      {parts.map((part, index) => (
        <Fragment key={index}>
          {part}
          {index < parts.length - 1 ? <wbr /> : null}
        </Fragment>
      ))}
    </span>
  );
}
