import type { ReactNode } from "react";
import {
  mmModuleTabBlurbBandClass,
  mmModuleTabBlurbTextClass,
} from "../../lib/ui/mm-module-tab-blurb";

type WorkspacePageProps = {
  children: ReactNode;
  dataTestId?: string;
};

/** A page of sections and their panels. The shell's header above it carries the title. */
export function WorkspacePage({ children, dataTestId }: WorkspacePageProps) {
  return (
    <div className="mm-page mm-workspace-page" data-testid={dataTestId}>
      {children}
    </div>
  );
}

type WorkspacePanelProps = {
  id: string;
  labelledBy: string;
  context?: ReactNode;
  children: ReactNode;
  contextTestId?: string;
};

export function WorkspacePanel({
  id,
  labelledBy,
  context,
  children,
  contextTestId,
}: WorkspacePanelProps) {
  return (
    <section
      id={id}
      className="mm-workspace-panel mm-bubble-stack"
      role="tabpanel"
      aria-labelledby={labelledBy}
    >
      {context !== undefined ? (
        <div className={mmModuleTabBlurbBandClass} data-testid={contextTestId}>
          <p className={mmModuleTabBlurbTextClass}>{context}</p>
        </div>
      ) : null}
      <div className="mm-workspace-panel__content mm-bubble-stack">
        {children}
      </div>
    </section>
  );
}
