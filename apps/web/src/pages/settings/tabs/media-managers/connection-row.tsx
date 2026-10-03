import type { ReactNode } from "react";

import { Panel } from "../../../../components/panels/panel";
import { SaveModelNote } from "../../save-model-note";

/**
 * The card every connected media manager or download client lives in, one row each. The card is where the
 * tab's save note goes, since a connection's changes save the moment they are made.
 */
export function ConnectionList({
  title,
  count,
  banner,
  testId,
  children,
}: {
  title: string;
  count: string;
  /** A message about the whole list, above its first row. */
  banner?: ReactNode;
  testId?: string;
  children: ReactNode;
}) {
  return (
    <Panel
      title={title}
      count={count}
      aside={<SaveModelNote model="instant" />}
      padded
      dataTestId={testId}
    >
      {banner}
      <ul className="mm-conn-rows">{children}</ul>
    </Panel>
  );
}

/**
 * One connection: its name, whether it answers and when it was last checked, with its buttons at the right, and
 * whatever else it has below.
 */
export function ConnectionRow({
  title,
  status,
  actions,
  testId,
  children,
}: {
  title: string;
  status: ReactNode;
  actions: ReactNode;
  testId: string;
  children?: ReactNode;
}) {
  return (
    <li className="mm-conn-row" data-testid={testId}>
      <div className="mm-conn-row__head">
        <h3 className="mm-conn-row__name">{title}</h3>
        {status}
        <div className="mm-conn-row__actions">{actions}</div>
      </div>
      {children}
    </li>
  );
}
