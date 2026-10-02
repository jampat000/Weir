import type { ReactNode } from "react";

/**
 * A block inside one of the editor's folding sections: a small heading, a line on what it is, and the block. Where
 * a section holds more than one thing (the media manager link, what that manager needs), each is one of these.
 */
export function EditorSubsection({
  title,
  detail,
  aside,
  children,
}: {
  title: string;
  detail?: string;
  /** A control at the heading's right edge. */
  aside?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section className="mm-editor-sub">
      <div className="mm-editor-sub__head">
        <h4 className="mm-editor-sub__title">{title}</h4>
        {aside}
      </div>
      {detail ? <p className="mm-editor-sub__detail">{detail}</p> : null}
      {children}
    </section>
  );
}
