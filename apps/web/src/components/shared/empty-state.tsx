import type { ReactNode } from "react";

/**
 * What a list says while it has nothing in it: what the things are for, and the one action that adds the first.
 * It sits where the list will be, so adding one visibly fills the space.
 */
export function EmptyState({
  title,
  action,
  testId,
  children,
}: {
  title: string;
  action: ReactNode;
  testId?: string;
  /** What it is for, in a sentence or two. */
  children: ReactNode;
}) {
  return (
    <section className="mm-empty-state" data-testid={testId}>
      <h2 className="mm-empty-state__title">{title}</h2>
      <p className="mm-empty-state__text">{children}</p>
      {action}
    </section>
  );
}
