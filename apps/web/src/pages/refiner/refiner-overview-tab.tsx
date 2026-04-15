import type { ReactNode } from "react";
import { PageLoading } from "../../components/shared/page-loading";
import { isHttpErrorFromApi, isLikelyNetworkFailure } from "../../lib/api/error-guards";
import { useRefinerJobsInspectionQuery } from "../../lib/refiner/jobs-inspection/queries";
import {
  useRefinerPathSettingsQuery,
  useRefinerOverviewStatsQuery,
  useRefinerRemuxRulesSettingsQuery,
  useRefinerRuntimeSettingsQuery,
} from "../../lib/refiner/queries";
import { refinerStreamLanguageLabel } from "../../lib/refiner/stream-language-options";
import type { RefinerRemuxRulesScopeSettings, RefinerRuntimeSettingsOut } from "../../lib/refiner/types";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";

export type RefinerOverviewOpenTab = "libraries" | "audio-subtitles" | "jobs" | "workers";

function workersOverviewCopy(rt: RefinerRuntimeSettingsOut): { headline: string; detail: string } {
  const n = rt.in_process_refiner_worker_count;
  if (!rt.in_process_workers_enabled || n <= 0) {
    return {
      headline: "Paused",
      detail: "Workers are off on this server. Queued jobs wait until they are enabled again.",
    };
  }
  if (n === 1) {
    return {
      headline: "One worker",
      detail: "Jobs run one at a time.",
    };
  }
  return {
    headline: `${n} workers`,
    detail: "Several jobs can run at once. Higher counts can still be limited by disk and database throughput.",
  };
}

function remuxDefaultsGlanceBody(rem: RefinerRemuxRulesScopeSettings): ReactNode {
  const pri = refinerStreamLanguageLabel(rem.primary_audio_lang);
  const sec = (rem.secondary_audio_lang ?? "").trim()
    ? refinerStreamLanguageLabel(rem.secondary_audio_lang)
    : null;
  const ter = (rem.tertiary_audio_lang ?? "").trim()
    ? refinerStreamLanguageLabel(rem.tertiary_audio_lang)
    : null;
  const langBits = [pri, sec, ter].filter((x) => x && x !== "—") as string[];
  const langLine = langBits.length ? langBits.join(" · ") : "—";

  const pol =
    rem.audio_preference_mode === "preferred_langs_strict"
      ? "Strict preferred languages"
      : rem.audio_preference_mode === "quality_all_languages"
        ? "Best quality (all languages)"
        : "Preferred languages, then quality";

  const sub =
    rem.subtitle_mode === "remove_all"
      ? "Remove all subtitles in output"
      : `Keep selected (${(rem.subtitle_langs_csv ?? "").trim() || "—"})`;

  return (
    <div className="space-y-1.5">
      <p>
        <span className="text-[var(--mm-text3)]">Audio languages:</span>{" "}
        <span className="font-medium text-[var(--mm-text1)]">{langLine}</span>
      </p>
      <p>
        <span className="text-[var(--mm-text3)]">Selection:</span>{" "}
        <span className="font-medium text-[var(--mm-text1)]">{pol}</span>
      </p>
      <p>
        <span className="text-[var(--mm-text3)]">Subtitles:</span>{" "}
        <span className="font-medium text-[var(--mm-text1)]">{sub}</span>
      </p>
    </div>
  );
}

function buildNeedsAttention(args: {
  workersEnabled: boolean;
  failedCount: number;
  watchedSet: boolean;
}): { text: string; target?: RefinerOverviewOpenTab }[] {
  const items: { text: string; target?: RefinerOverviewOpenTab }[] = [];
  if (!args.workersEnabled) {
    items.push({
      text: "Refiner workers are off on this server — new jobs wait until workers are turned back on.",
      target: "workers",
    });
  }
  if (args.failedCount > 0) {
    items.push({
      text:
        args.failedCount === 1
          ? "One job is in the failed list — open Jobs to review."
          : `${args.failedCount} jobs are in the failed list — open Jobs to review.`,
      target: "jobs",
    });
  }
  if (!args.watchedSet) {
    items.push({
      text: "No Movies or TV watched folder yet — set at least one under Libraries before scans or passes.",
      target: "libraries",
    });
  }
  return items.slice(0, 4);
}

function tabActionLabel(id: RefinerOverviewOpenTab): string {
  switch (id) {
    case "libraries":
      return "Open Libraries";
    case "audio-subtitles":
      return "Open Audio & subtitles";
    case "jobs":
      return "Open Jobs";
    case "workers":
      return "Open Workers";
    default: {
      const _e: never = id;
      return _e;
    }
  }
}

const NEEDS_ATTENTION_ORDER: RefinerOverviewOpenTab[] = ["workers", "jobs", "libraries", "audio-subtitles"];

function RefinerOverviewNeedsAttention({
  items,
  onOpenTab,
}: {
  items: { text: string; target?: RefinerOverviewOpenTab }[];
  onOpenTab?: (t: RefinerOverviewOpenTab) => void;
}) {
  const empty = items.length === 0;
  const actionTargets = NEEDS_ATTENTION_ORDER.filter((t) => items.some((row) => row.target === t));

  return (
    <section
      className="mm-card mm-dash-card mm-fetcher-module-surface"
      aria-labelledby="refiner-overview-needs-attention-heading"
      data-testid="refiner-overview-needs-attention"
    >
      <h2 id="refiner-overview-needs-attention-heading" className="mm-card__title text-lg">
        Needs attention
      </h2>
      <div className="mm-card__body mt-5 text-sm text-[var(--mm-text2)]">
        {empty ? (
          <p>Nothing stands out right now.</p>
        ) : (
          <>
            <ul className="list-none space-y-3 border-l-2 border-[var(--mm-border)] pl-3.5">
              {items.map((row, i) => (
                <li key={`${row.text}-${i}`} className="leading-snug text-[var(--mm-text1)]">
                  {row.text}
                </li>
              ))}
            </ul>
            {onOpenTab && actionTargets.length > 0 ? (
              <div className="mt-5 flex flex-wrap gap-2.5 border-t border-[var(--mm-border)] pt-4">
                {actionTargets.map((target) => (
                  <button
                    key={target}
                    type="button"
                    className={mmActionButtonClass({ variant: "secondary" })}
                    onClick={() => onOpenTab(target)}
                  >
                    {tabActionLabel(target)}
                  </button>
                ))}
              </div>
            ) : null}
          </>
        )}
      </div>
    </section>
  );
}

function RefinerOverviewLoadError({ err }: { err: unknown }) {
  return (
    <div className="mm-page__intro" data-testid="refiner-overview-load-error">
      <p className="mm-page__lead">
        {isLikelyNetworkFailure(err)
          ? "Could not reach the MediaMop API. Check that the backend is running."
          : isHttpErrorFromApi(err)
            ? "The server refused this request. Sign in again or check API logs."
            : "Could not load part of the Refiner overview."}
      </p>
    </div>
  );
}

/** Refiner module Overview — summary, attention, and tab switches only (no settings forms). */
export function RefinerOverviewTab({
  onOpenTab,
}: {
  onOpenTab?: (t: RefinerOverviewOpenTab) => void;
} = {}) {
  const pathSettings = useRefinerPathSettingsQuery();
  const runtime = useRefinerRuntimeSettingsQuery();
  const remuxRules = useRefinerRemuxRulesSettingsQuery();
  const overviewStats = useRefinerOverviewStatsQuery();
  const pending = useRefinerJobsInspectionQuery("pending");
  const leased = useRefinerJobsInspectionQuery("leased");
  const failed = useRefinerJobsInspectionQuery("failed");

  const blocking = pathSettings.isError ? pathSettings.error : runtime.isError ? runtime.error : null;

  if (blocking) {
    return <RefinerOverviewLoadError err={blocking} />;
  }

  if (pathSettings.isPending || runtime.isPending) {
    return <PageLoading label="Loading Refiner overview" />;
  }

  if (!pathSettings.data || !runtime.data) {
    return <PageLoading label="Loading Refiner overview" />;
  }

  const watchedSet =
    Boolean((pathSettings.data.refiner_watched_folder ?? "").trim()) ||
    Boolean((pathSettings.data.refiner_tv_watched_folder ?? "").trim());
  const outputSet = Boolean((pathSettings.data.refiner_output_folder ?? "").trim());
  const tvWatchedSet = Boolean((pathSettings.data.refiner_tv_watched_folder ?? "").trim());
  const tvOutputSet = Boolean((pathSettings.data.refiner_tv_output_folder ?? "").trim());

  const pendingN = pending.data?.jobs.length ?? 0;
  const leasedN = leased.data?.jobs.length ?? 0;
  const failedN = failed.data?.jobs.length ?? 0;
  const failedReady = !failed.isPending && !failed.isError;

  const workers = workersOverviewCopy(runtime.data);

  const foldersBody = (
    <div className="space-y-1.5">
      <p>
        <span className="text-[var(--mm-text3)]">Movies watched:</span>{" "}
        <span className="font-medium text-[var(--mm-text1)]">
          {(pathSettings.data.refiner_watched_folder ?? "").trim() ? "Set" : "Not set"}
        </span>
      </p>
      <p>
        <span className="text-[var(--mm-text3)]">TV watched:</span>{" "}
        <span className="font-medium text-[var(--mm-text1)]">{tvWatchedSet ? "Set" : "Not set"}</span>
      </p>
      <p>
        <span className="text-[var(--mm-text3)]">Movies output:</span>{" "}
        <span className="font-medium text-[var(--mm-text1)]">{outputSet ? "Set" : "Not set"}</span>
      </p>
      <p>
        <span className="text-[var(--mm-text3)]">TV output:</span>{" "}
        <span className="font-medium text-[var(--mm-text1)]">{tvOutputSet ? "Set" : "Not set"}</span>
      </p>
    </div>
  );

  const queueBody = (
    <div className="space-y-1.5">
      <p>
        <span className="text-[var(--mm-text3)]">Waiting:</span>{" "}
        <span className="font-medium text-[var(--mm-text1)]">{pendingN === 0 ? "None" : `${pendingN} job(s)`}</span>
      </p>
      <p>
        <span className="text-[var(--mm-text3)]">Running:</span>{" "}
        <span className="font-medium text-[var(--mm-text1)]">{leasedN === 0 ? "None" : `${leasedN} job(s)`}</span>
      </p>
      <p>
        <span className="text-[var(--mm-text3)]">Failed list:</span>{" "}
        <span className="font-medium text-[var(--mm-text1)]">
          {failed.isPending
            ? "…"
            : failed.isError
              ? "Can't load right now"
              : failedN === 0
                ? "Empty"
                : `${failedN} in list (up to 50 shown in Jobs)`}
        </span>
      </p>
    </div>
  );

  const workerBody = (
    <div className="space-y-2">
      <p className="font-medium text-[var(--mm-text1)]">{workers.headline}</p>
      <p className="text-xs leading-snug text-[var(--mm-text3)]">{workers.detail}</p>
    </div>
  );

  const remuxBody =
    remuxRules.isPending ? (
      <p className="text-[var(--mm-text3)]">Loading…</p>
    ) : remuxRules.isError ? (
      <p className="text-[var(--mm-text3)]">Could not load file rules.</p>
    ) : remuxRules.data ? (
      <div className="space-y-3">
        <div>
          <p className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">Movies</p>
          {remuxDefaultsGlanceBody(remuxRules.data.movie)}
        </div>
        <div>
          <p className="text-xs font-semibold uppercase tracking-wide text-[var(--mm-text3)]">TV</p>
          {remuxDefaultsGlanceBody(remuxRules.data.tv)}
        </div>
      </div>
    ) : (
      <p className="text-[var(--mm-text3)]">—</p>
    );
  const statsBody =
    overviewStats.isPending || overviewStats.isError || !overviewStats.data ? (
      <p className="text-[var(--mm-text3)]">Loading…</p>
    ) : (
      <div className="space-y-3">
        <p>
          <span className="text-xs uppercase tracking-wide text-[var(--mm-text3)]">Files processed</span>
          <span className="mt-0.5 block text-xl font-semibold leading-tight text-[var(--mm-text1)]">
            {overviewStats.data.files_processed}
          </span>
        </p>
        <p>
          <span className="text-xs uppercase tracking-wide text-[var(--mm-text3)]">Success rate</span>
          <span className="mt-0.5 block text-base font-semibold leading-tight text-[var(--mm-text1)]">
            {overviewStats.data.success_rate_percent}%
          </span>
        </p>
        <p>
          <span className="text-xs uppercase tracking-wide text-[var(--mm-text3)]">Space saved</span>
          <span className="mt-0.5 block text-base font-semibold leading-tight text-[var(--mm-text1)]">
            {overviewStats.data.space_saved_available && overviewStats.data.space_saved_gb !== null
              ? `${overviewStats.data.space_saved_gb.toFixed(1)} GB`
              : "Not available yet"}
          </span>
        </p>
      </div>
    );

  const attentionItems = buildNeedsAttention({
    workersEnabled: runtime.data.in_process_workers_enabled,
    failedCount: failedReady ? failedN : 0,
    watchedSet,
  });

  return (
    <div data-testid="refiner-overview-panel" className="w-full min-w-0 space-y-6 sm:space-y-7">
      <section
        className="mm-card mm-dash-card mm-fetcher-module-surface"
        aria-labelledby="refiner-overview-at-a-glance-heading"
        data-testid="refiner-overview-at-a-glance"
      >
        <h2 id="refiner-overview-at-a-glance-heading" className="mm-card__title text-lg">
          At a glance
        </h2>
        <div className="mm-card__body mt-5 grid gap-4 sm:grid-cols-2 sm:gap-x-5 sm:gap-y-5 lg:grid-cols-3 lg:gap-x-5 lg:gap-y-5">
          <AtGlanceCard order="1" title="Last 30 days" body={statsBody} data-testid="refiner-overview-last-30-days" />
          <AtGlanceCard order="2" title="Libraries" body={foldersBody} />
          <AtGlanceCard order="3" title="Job queue" body={queueBody} />
          <AtGlanceCard order="4" title="Workers" body={workerBody} />
          <AtGlanceCard
            order="5"
            title="Audio & subtitles"
            body={remuxBody}
            footer={
              onOpenTab ? (
                <button
                  type="button"
                  className={mmActionButtonClass({ variant: "secondary" })}
                  onClick={() => onOpenTab("audio-subtitles")}
                >
                  Open Audio & subtitles
                </button>
              ) : undefined
            }
            data-testid="refiner-overview-audio-subtitles-glance"
          />
        </div>
      </section>

      <RefinerOverviewNeedsAttention items={attentionItems} onOpenTab={onOpenTab} />

      <section
        className="mm-card mm-dash-card mm-fetcher-module-surface"
        aria-labelledby="refiner-overview-next-heading"
        data-testid="refiner-overview-go-deeper"
      >
        <h2 id="refiner-overview-next-heading" className="mm-card__title text-lg">
          Next steps
        </h2>
        <div className="mm-card__body mt-5 space-y-3 text-sm text-[var(--mm-text2)]">
          <p>
            Outcomes land on <strong className="text-[var(--mm-text1)]">Activity</strong>. Use the other tabs for paths,
            audio/subtitle defaults, jobs, and worker settings.
          </p>
          {onOpenTab ? (
            <div className="flex flex-wrap gap-2.5 pt-1">
              <button type="button" className={mmActionButtonClass({ variant: "secondary" })} onClick={() => onOpenTab("libraries")}>
                Libraries
              </button>
              <button
                type="button"
                className={mmActionButtonClass({ variant: "secondary" })}
                onClick={() => onOpenTab("audio-subtitles")}
              >
                Audio & subtitles
              </button>
              <button type="button" className={mmActionButtonClass({ variant: "secondary" })} onClick={() => onOpenTab("jobs")}>
                Jobs
              </button>
              <button type="button" className={mmActionButtonClass({ variant: "secondary" })} onClick={() => onOpenTab("workers")}>
                Workers
              </button>
            </div>
          ) : null}
        </div>
      </section>
    </div>
  );
}

function AtGlanceCard({
  title,
  body,
  order,
  footer,
  "data-testid": dataTestId,
}: {
  title: string;
  body: ReactNode;
  order: "1" | "2" | "3" | "4" | "5";
  footer?: ReactNode;
  "data-testid"?: string;
}) {
  return (
    <div
      className="flex h-full flex-col gap-3 rounded-md border border-[var(--mm-border)] bg-[var(--mm-card-bg)]/70 p-5 text-sm"
      data-at-glance-order={order}
      data-testid={dataTestId}
    >
      <h3 className="text-sm font-semibold tracking-wide text-[var(--mm-text1)]">{title}</h3>
      <div className="min-h-0 flex-1 text-[var(--mm-text2)]">{body}</div>
      {footer ? <div className="mt-auto border-t border-[var(--mm-border)] pt-3">{footer}</div> : null}
    </div>
  );
}
