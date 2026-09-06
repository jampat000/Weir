import type { ReactNode } from "react";
import {
  mmModuleTabBlurbBandClass,
  mmModuleTabBlurbTextClass,
} from "../../lib/ui/mm-module-tab-blurb";
import { mmSectionTabClass } from "../../lib/ui/mm-control-roles";

type WorkspaceVariant = "rail" | "tabs";

function classes(...values: Array<string | undefined>): string {
  return values.filter(Boolean).join(" ");
}

type WorkspacePageProps = {
  variant: WorkspaceVariant;
  eyebrow: string;
  title: string;
  description: ReactNode;
  children: ReactNode;
  dataTestId?: string;
  descriptionClassName?: string;
};

export function WorkspacePage({
  variant,
  eyebrow,
  title,
  description,
  children,
  dataTestId,
  descriptionClassName,
}: WorkspacePageProps) {
  return (
    <div
      className={classes(
        "mm-page",
        "mm-workspace-page",
        `mm-workspace-page--${variant}`,
      )}
      data-testid={dataTestId}
    >
      <header
        className={classes(
          "mm-page__intro",
          "mm-workspace-page__intro",
          `mm-workspace-page__intro--${variant}`,
        )}
      >
        <p className="mm-page__eyebrow">{eyebrow}</p>
        <h1 className="mm-page__title">{title}</h1>
        <p
          className={classes(
            variant === "rail" ? "mm-page__lead" : "mm-page__subtitle",
            descriptionClassName,
          )}
        >
          {description}
        </p>
      </header>
      {children}
    </div>
  );
}

export type WorkspaceTabOption<Id extends string> = Readonly<{
  id: Id;
  label: string;
}>;

type WorkspaceTabListProps<Id extends string> = {
  tabs: readonly WorkspaceTabOption<Id>[];
  activeId: Id;
  onSelect: (id: Id) => void;
  ariaLabel: string;
  idPrefix: string;
  panelId: string;
  dataTestId?: string;
};

export function WorkspaceTabList<Id extends string>({
  tabs,
  activeId,
  onSelect,
  ariaLabel,
  idPrefix,
  panelId,
  dataTestId,
}: WorkspaceTabListProps<Id>) {
  return (
    <nav
      className="mm-workspace-tabs"
      aria-label={ariaLabel}
      data-testid={dataTestId}
    >
      <div
        role="tablist"
        aria-label={ariaLabel}
        className="mm-workspace-tabs__list"
      >
        {tabs.map(({ id, label }) => {
          const selected = activeId === id;
          return (
            <button
              key={id}
              type="button"
              role="tab"
              id={`${idPrefix}-${id}`}
              aria-controls={panelId}
              aria-selected={selected}
              className={mmSectionTabClass(selected)}
              onClick={() => onSelect(id)}
            >
              {label}
            </button>
          );
        })}
      </div>
    </nav>
  );
}

type WorkspacePanelProps = {
  id: string;
  labelledBy: string;
  context: ReactNode;
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
      <div className={mmModuleTabBlurbBandClass} data-testid={contextTestId}>
        <p className={mmModuleTabBlurbTextClass}>{context}</p>
      </div>
      <div className="mm-workspace-panel__content mm-bubble-stack">
        {children}
      </div>
    </section>
  );
}

type WorkspaceRailLayoutProps = {
  navigation: ReactNode;
  navigationLabel: string;
  panelId: string;
  panelLabelledBy: string;
  children: ReactNode;
  dataTestId?: string;
};

export function WorkspaceRailLayout({
  navigation,
  navigationLabel,
  panelId,
  panelLabelledBy,
  children,
  dataTestId,
}: WorkspaceRailLayoutProps) {
  return (
    <div
      className="mm-workspace-layout mm-workspace-layout--rail"
      data-testid={dataTestId}
    >
      <nav
        className="mm-workspace-rail__navigation"
        aria-label={navigationLabel}
      >
        <div
          className="mm-workspace-rail__tabs"
          role="tablist"
          aria-label={navigationLabel}
        >
          {navigation}
        </div>
      </nav>
      <section
        id={panelId}
        className="mm-workspace-rail__content"
        role="tabpanel"
        aria-labelledby={panelLabelledBy}
      >
        {children}
      </section>
    </div>
  );
}
