import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import {
  WorkspacePage,
  WorkspacePanel,
  WorkspaceTabList,
  type WorkspaceTabOption,
} from "../../components/shared/workspace-shell";
import { usePrunerInstancesQuery } from "../../lib/pruner/queries";
import { PRUNER_TAB_BLURBS } from "./pruner-page-constants";
import type { TopTab } from "./pruner-page-types";
import { ProviderConfigurationWorkspace } from "./pruner-provider-configuration-workspace";
import { TopLevelJobs } from "./pruner-top-level-jobs";
import { TopLevelOverview } from "./pruner-top-level-overview";

type PrunerPrimaryTab = Exclude<TopTab, "schedule">;

const PRUNER_TABS = [
  { id: "overview", label: "Overview" },
  { id: "emby", label: "Emby" },
  { id: "jellyfin", label: "Jellyfin" },
  { id: "plex", label: "Plex" },
  { id: "jobs", label: "Jobs" },
] as const satisfies readonly WorkspaceTabOption<PrunerPrimaryTab>[];

export function PrunerInstancesListPage() {
  const q = usePrunerInstancesQuery();
  const [searchParams, setSearchParams] = useSearchParams();
  const [topTab, setTopTab] = useState<TopTab>(() => {
    const candidate = searchParams.get("tab") as TopTab | null;
    return candidate &&
      ["overview", "emby", "jellyfin", "plex", "jobs", "schedule"].includes(
        candidate,
      )
      ? candidate
      : "overview";
  });
  useEffect(() => {
    const candidate = searchParams.get("tab") as TopTab | null;
    if (
      candidate &&
      ["overview", "emby", "jellyfin", "plex", "jobs", "schedule"].includes(
        candidate,
      )
    ) {
      setTopTab(candidate);
    } else {
      setTopTab("overview");
    }
  }, [searchParams]);
  const selectTopTab = (next: TopTab) => {
    setTopTab(next);
    const params = new URLSearchParams(searchParams);
    if (next === "overview") params.delete("tab");
    else params.set("tab", next);
    setSearchParams(params, { replace: true });
  };
  const instances = q.data ?? [];
  const selectedPrimaryTab: PrunerPrimaryTab =
    topTab === "schedule" ? "emby" : topTab;

  return (
    <WorkspacePage
      eyebrow="MediaMop"
      title="Pruner"
      dataTestId="pruner-scope-page"
      descriptionClassName="max-w-3xl"
      description={
        <>
          Library cleanup for{" "}
          <strong className="text-[var(--mm-text)]">Emby</strong>,{" "}
          <strong className="text-[var(--mm-text)]">Jellyfin</strong>, and{" "}
          <strong className="text-[var(--mm-text)]">Plex</strong>. Each provider
          tab has Connection, Cleanup, and Schedule for that server.
        </>
      }
    >
      <WorkspaceTabList
        tabs={PRUNER_TABS}
        activeId={selectedPrimaryTab}
        onSelect={selectTopTab}
        ariaLabel="Pruner sections"
        idPrefix="pruner-tab"
        panelId="pruner-panel"
        dataTestId="pruner-top-level-tabs"
      />

      <WorkspacePanel
        id="pruner-panel"
        labelledBy={`pruner-tab-${selectedPrimaryTab}`}
        context={PRUNER_TAB_BLURBS[topTab]}
        contextTestId="pruner-tab-blurb"
      >
        {q.isLoading ? (
          <p className="text-sm text-[var(--mm-text2)]">
            Loading provider instances...
          </p>
        ) : null}
        {q.isError ? (
          <p className="text-sm text-red-600">{(q.error as Error).message}</p>
        ) : null}
        {!q.isLoading && !q.isError ? (
          topTab === "overview" ? (
            <TopLevelOverview
              instances={instances}
              onOpenProviderTab={(t) => selectTopTab(t)}
              onNavigateTopTab={(t) => selectTopTab(t)}
            />
          ) : topTab === "jobs" ? (
            <TopLevelJobs instances={instances} />
          ) : topTab === "schedule" ? (
            <ProviderConfigurationWorkspace
              provider="emby"
              allInstances={instances}
              initialSection="schedule"
            />
          ) : (
            <ProviderConfigurationWorkspace
              provider={topTab}
              allInstances={instances}
            />
          )
        ) : null}
      </WorkspacePanel>
    </WorkspacePage>
  );
}
