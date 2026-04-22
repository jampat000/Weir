import { Link } from "react-router-dom";
import { PageLoading } from "../../components/shared/page-loading";
import { useActivityRecentQuery, activityRecentKey } from "../../lib/activity/queries";
import { useActivityStreamInvalidation } from "../../lib/activity/use-activity-stream-invalidation";
import type { ActivityEventItem } from "../../lib/api/types";
import { isHttpErrorFromApi, isLikelyNetworkFailure } from "../../lib/api/error-guards";
import { dashboardStatusKey, useDashboardStatusQuery } from "../../lib/dashboard/queries";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import { useAppDateFormatter } from "../../lib/ui/mm-format-date";

type ModuleKey = "refiner" | "pruner" | "subber";
type ModuleStatus = "Healthy" | "Review needed" | "Active";

type ModuleCardData = {
  key: ModuleKey;
  name: string;
  status: ModuleStatus;
  summary: string;
  tvLine: string;
  moviesLine: string;
  actionLabel: string;
  actionTo: string;
};

function shortLastActivity(items: ActivityEventItem[], fmt: (iso: string) => string): string {
  if (items.length === 0) {
    return "No recent activity";
  }
  const ev = items[0];
  const moduleLabel = ev.module ? `${ev.module[0].toUpperCase()}${ev.module.slice(1)}` : "Module";
  let summary = "Update recorded";
  if (ev.module === "auth") {
    summary = "Sign-in activity updated";
  } else if (ev.module === "settings") {
    summary = "Settings updated";
  }
  return `${moduleLabel}: ${summary} · ${fmt(ev.created_at)}`;
}

function matchesAttentionText(ev: ActivityEventItem): boolean {
  const text = `${ev.title} ${ev.detail || ""}`.toLowerCase();
  return /(failed|error|needs|review|missing|stopped|follow-up)/.test(text);
}

function moduleEvents(items: ActivityEventItem[], module: ModuleKey): ActivityEventItem[] {
  return items.filter((i) => i.module === module);
}

function deriveModuleCard(module: ModuleKey, items: ActivityEventItem[]): ModuleCardData {
  const events = moduleEvents(items, module);
  const hasAttention = events.some(matchesAttentionText);
  const status: ModuleStatus = hasAttention ? "Review needed" : events.length > 0 ? "Active" : "Healthy";
  const tvMentions = events.filter((e) => /(tv|episode|show)/i.test(`${e.title} ${e.detail || ""}`)).length;
  const movieMentions = events.filter((e) => /(movie|movies|film)/i.test(`${e.title} ${e.detail || ""}`)).length;
  const title = module[0].toUpperCase() + module.slice(1);
  return {
    key: module,
    name: title,
    status,
    summary:
      status === "Review needed"
        ? "Recent items may need review."
        : events.length > 0
          ? "Module activity is currently visible."
          : "No recent module activity.",
    tvLine: tvMentions > 0 ? `${tvMentions} TV updates in recent signals` : "TV: No recent signal",
    moviesLine: movieMentions > 0 ? `${movieMentions} movie updates in recent signals` : "Movies: No recent signal",
    actionLabel: `Open ${title}`,
    actionTo: `/app/${module}`,
  };
}

function StatusTile({ label, value }: { label: string; value: string }) {
  return (
    <section className="rounded-md border border-[var(--mm-border)] bg-[var(--mm-card-bg)] px-3 py-2.5">
      <p className="text-xs font-medium uppercase tracking-wide text-[var(--mm-text3)]">{label}</p>
      <p className="mt-1 text-sm font-semibold text-[var(--mm-text1)]">{value}</p>
    </section>
  );
}

export function DashboardPage() {
  const fmt = useAppDateFormatter();
  useActivityStreamInvalidation(dashboardStatusKey);
  useActivityStreamInvalidation(activityRecentKey);

  const dash = useDashboardStatusQuery();
  const recent = useActivityRecentQuery();

  if (dash.isPending) {
    return <PageLoading label="Loading dashboard" />;
  }
  if (dash.isError) {
    const err = dash.error;
    return (
      <div className="mm-page">
        <header className="mm-page__intro">
          <h1 className="mm-page__title">Dashboard</h1>
          <p className="mm-page__lead">
            {isLikelyNetworkFailure(err)
              ? "Could not reach the MediaMop API. Check that the backend is running."
              : isHttpErrorFromApi(err)
                ? "The server refused this request. Sign in again or check API logs."
                : "Something went wrong loading dashboard status."}
          </p>
        </header>
      </div>
    );
  }

  const recentItems = recent.data?.items ?? [];
  const moduleCards: ModuleCardData[] = [
    deriveModuleCard("refiner", recentItems),
    deriveModuleCard("pruner", recentItems),
    deriveModuleCard("subber", recentItems),
  ];
  const modulesNeedingAttentionTotal = moduleCards.filter((m) => m.status === "Review needed").length;
  const activeModuleCount = moduleCards.filter((m) => m.status === "Active").length;

  const overallStatus =
    !dash.data.system.healthy
      ? "Review needed"
      : modulesNeedingAttentionTotal > 0
        ? "Review needed"
        : activeModuleCount > 0
          ? "Active"
          : "Healthy";
  const lastActivity = shortLastActivity(recentItems, fmt);

  const attentionItems: string[] = [];
  for (const m of moduleCards.filter((x) => x.status === "Review needed")) {
    attentionItems.push(`${m.name}: ${m.summary}`);
  }

  return (
    <div className="mm-page" data-testid="dashboard-page">
      <header className="mm-page__intro">
        <h1 className="mm-page__title">Dashboard</h1>
        <p className="mm-page__lead">See what needs attention across MediaMop and which modules are active now.</p>
      </header>

      <section className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4" data-testid="dashboard-status-strip">
        <StatusTile label="Overall status" value={overallStatus} />
        <StatusTile
          label="Modules needing attention"
          value={
            modulesNeedingAttentionTotal === 0
              ? "None detected"
              : `${modulesNeedingAttentionTotal} module${modulesNeedingAttentionTotal === 1 ? "" : "s"} need attention`
          }
        />
        <StatusTile label="Active modules" value={activeModuleCount > 0 ? String(activeModuleCount) : "None"} />
        <StatusTile label="Last activity" value={lastActivity} />
      </section>

      <section className="mt-5 grid gap-4 sm:grid-cols-2 xl:grid-cols-3" data-testid="dashboard-module-cards">
        {moduleCards.map((m) => (
          <article key={m.key} className="mm-card mm-dash-card flex h-full flex-col gap-3">
            <div className="flex items-start justify-between gap-2">
              <h2 className="text-base font-semibold text-[var(--mm-text1)]">{m.name}</h2>
              <span className="rounded border border-[var(--mm-border)] bg-[var(--mm-surface2)] px-2 py-0.5 text-xs text-[var(--mm-text2)]">
                {m.status}
              </span>
            </div>
            <p className="text-sm text-[var(--mm-text2)]">{m.summary}</p>
            <p className="text-sm text-[var(--mm-text2)]">{m.tvLine}</p>
            <p className="text-sm text-[var(--mm-text2)]">{m.moviesLine}</p>
            <div className="mt-auto pt-1">
              <Link to={m.actionTo} className={mmActionButtonClass({ variant: "secondary" })}>
                {m.actionLabel}
              </Link>
            </div>
          </article>
        ))}
      </section>

      <section className="mm-card mm-dash-card mt-6" data-testid="dashboard-needs-attention">
        <h2 className="mm-card__title">Needs attention</h2>
        <div className="mm-card__body mm-card__body--tight space-y-2 text-sm text-[var(--mm-text2)]">
          {attentionItems.length > 0 ? (
            attentionItems.map((line) => <p key={line}>{line}</p>)
          ) : (
            <p>No module attention signals are currently detected.</p>
          )}
        </div>
      </section>

      <section className="mm-card mm-dash-card mt-6" data-testid="dashboard-active-work">
        <h2 className="mm-card__title">Active work</h2>
        <div className="mm-card__body mm-card__body--tight space-y-2 text-sm text-[var(--mm-text2)]">
          <p>
            {activeModuleCount > 0
              ? `${activeModuleCount} module${activeModuleCount === 1 ? "" : "s"} show recent activity.`
              : "No active module signals detected."}
          </p>
        </div>
      </section>
    </div>
  );
}
