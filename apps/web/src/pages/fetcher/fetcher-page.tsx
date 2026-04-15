import { useState } from "react";
import { useMeQuery } from "../../lib/auth/queries";
import {
  FetcherArrOperatorSettingsSection,
  type FetcherArrSettingsTabId,
} from "./fetcher-arr-operator-settings";
import { FetcherFailedImportsWorkspace } from "./fetcher-failed-imports-workspace";
import { FETCHER_TAB_RADARR_LABEL, FETCHER_TAB_SONARR_LABEL } from "./fetcher-display-names";
import { fetcherSectionTabClass } from "./fetcher-menu-button";
import { FetcherOverviewTab } from "./fetcher-overview-tab";

type FetcherPageTabId = "overview" | FetcherArrSettingsTabId | "failed-imports";

export function FetcherPage() {
  const me = useMeQuery();
  const [tab, setTab] = useState<FetcherPageTabId>("overview");

  const tabs: { id: FetcherPageTabId; label: string }[] = [
    { id: "overview", label: "Overview" },
    { id: "connections", label: "Connections" },
    { id: "sonarr", label: FETCHER_TAB_SONARR_LABEL },
    { id: "radarr", label: FETCHER_TAB_RADARR_LABEL },
    { id: "failed-imports", label: "Failed imports" },
  ];

  return (
    <div className="mm-page">
      <header className="mm-page__intro !mb-0">
        <p className="mm-page__eyebrow">MediaMop</p>
        <h1 className="mm-page__title">Fetcher</h1>
        <p className="mm-page__subtitle">
          Fetcher helps you search for missing TV shows and movies, and upgrade existing ones when a better version is
          available.
        </p>
      </header>

      <nav
        className="mt-3 flex flex-wrap gap-2.5 border-b border-[var(--mm-border)] pb-3.5 sm:mt-4"
        aria-label="Fetcher sections"
        data-testid="fetcher-section-tabs"
      >
        {tabs.map(({ id, label }) => (
          <button
            key={id}
            type="button"
            role="tab"
            aria-selected={tab === id}
            className={fetcherSectionTabClass(tab === id)}
            onClick={() => setTab(id)}
          >
            {label}
          </button>
        ))}
      </nav>

      <div className="mt-6 sm:mt-7" role="tabpanel" aria-label={tabs.find((t) => t.id === tab)?.label}>
        {tab === "overview" ? <FetcherOverviewTab onOpenSection={(target) => setTab(target)} /> : null}

        {tab === "failed-imports" ? <FetcherFailedImportsWorkspace /> : null}

        {tab === "connections" || tab === "sonarr" || tab === "radarr" ? (
          <FetcherArrOperatorSettingsSection role={me.data?.role} activeTab={tab} />
        ) : null}
      </div>
    </div>
  );
}
