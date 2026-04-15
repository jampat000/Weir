import { useState } from "react";
import { Link } from "react-router-dom";
import { fetcherSectionTabClass } from "../fetcher/fetcher-menu-button";
import { RefinerJobsInspectionSection } from "./refiner-jobs-inspection-section";
import { RefinerOverviewTab, type RefinerOverviewOpenTab } from "./refiner-overview-tab";
import { RefinerPathSettingsSection } from "./refiner-path-settings-section";
import { RefinerRuntimeSettingsSection } from "./refiner-runtime-settings-section";
import { RefinerWatchedFolderScanSection } from "./refiner-watched-folder-scan-section";
import { RefinerRemuxSection } from "./refiner-remux-section";

type RefinerPageTabId = "overview" | "libraries" | "audio-subtitles" | "jobs" | "workers";

export function RefinerPage() {
  const [tab, setTab] = useState<RefinerPageTabId>("overview");

  const tabs: { id: RefinerPageTabId; label: string }[] = [
    { id: "overview", label: "Overview" },
    { id: "libraries", label: "Libraries" },
    { id: "audio-subtitles", label: "Audio & subtitles" },
    { id: "jobs", label: "Jobs" },
    { id: "workers", label: "Workers" },
  ];

  const openFromOverview = (target: RefinerOverviewOpenTab) => {
    const map: Record<RefinerOverviewOpenTab, RefinerPageTabId> = {
      libraries: "libraries",
      "audio-subtitles": "audio-subtitles",
      jobs: "jobs",
      workers: "workers",
    };
    setTab(map[target]);
  };

  return (
    <div className="mm-page w-full min-w-0" data-testid="refiner-scope-page">
      <header className="mm-page__intro !mb-0">
        <p className="mm-page__eyebrow">MediaMop</p>
        <h1 className="mm-page__title">Refiner</h1>
        <p className="mm-page__subtitle">
          Refiner safely remuxes your existing <strong className="text-[var(--mm-text)]">Movies</strong> and{" "}
          <strong className="text-[var(--mm-text)]">TV</strong> files into your preferred audio and subtitle layout,
          then tracks each run from queued to complete on{" "}
          <Link
            className="font-semibold text-[var(--mm-text)] underline-offset-2 hover:underline"
            to="/app/activity"
          >
            Activity
          </Link>
          .
        </p>
      </header>

      <nav
        className="mt-3 flex flex-wrap gap-2.5 border-b border-[var(--mm-border)] pb-3.5 sm:mt-4"
        aria-label="Refiner sections"
        data-testid="refiner-section-tabs"
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

      <div
        className="mt-6 w-full min-w-0 sm:mt-7"
        role="tabpanel"
        aria-label={tabs.find((t) => t.id === tab)?.label}
      >
        {tab === "overview" ? <RefinerOverviewTab onOpenTab={openFromOverview} /> : null}

        {tab === "libraries" ? (
          <div className="flex w-full min-w-0 flex-col gap-6">
            <RefinerPathSettingsSection />
            <RefinerWatchedFolderScanSection />
          </div>
        ) : null}

        {tab === "audio-subtitles" ? <RefinerRemuxSection /> : null}

        {tab === "jobs" ? <RefinerJobsInspectionSection /> : null}

        {tab === "workers" ? <RefinerRuntimeSettingsSection /> : null}
      </div>
    </div>
  );
}
