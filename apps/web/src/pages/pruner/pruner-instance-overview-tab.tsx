import { Link, useOutletContext } from "react-router-dom";
import type { PrunerScopeSummary, PrunerServerInstance } from "../../lib/pruner/api";
import { formatPrunerDateTime, plexUnsupportedRuleFamilies } from "./pruner-ui-utils";

type Ctx = { instanceId: number; instance: PrunerServerInstance | undefined };

function scopeHeading(media: string): string {
  return media === "tv" ? "TV shows" : "Movies";
}

function activeRuleLines(scope: PrunerScopeSummary, provider: string): string[] {
  const lines: string[] = [];
  if (scope.missing_primary_media_reported_enabled) {
    lines.push("Delete items missing a main poster or episode image — when your server supports it");
  }
  if (scope.never_played_stale_reported_enabled) {
    lines.push(`Delete never-started titles older than ${scope.never_played_min_age_days} days`);
  }
  if (scope.media_scope === "tv" && scope.watched_tv_reported_enabled) {
    lines.push("Delete watched TV episodes");
  }
  if (scope.media_scope === "movies") {
    if (scope.watched_movies_reported_enabled) lines.push("Delete watched movies");
    if (scope.watched_movie_low_rating_reported_enabled) {
      const cap =
        provider === "plex"
          ? `Delete watched Plex movies rated below ${scope.watched_movie_low_rating_max_plex_audience_rating} (0–10 audience score)`
          : `Delete watched movies rated below ${scope.watched_movie_low_rating_max_jellyfin_emby_community_rating} (0–10 community score)`;
      lines.push(cap);
    }
    if (scope.unwatched_movie_stale_reported_enabled) {
      lines.push(`Delete unwatched movies older than ${scope.unwatched_movie_stale_min_age_days} days`);
    }
  }
  if (lines.length === 0) {
    lines.push("No cleanup rules are turned on for this tab yet.");
  }
  return lines;
}

function filterCountSummary(scope: PrunerScopeSummary): { label: string; count: number }[] {
  return [
    { label: "Genres narrowed to", count: (scope.preview_include_genres ?? []).length },
    { label: "Names narrowed to", count: (scope.preview_include_people ?? []).length },
    { label: "Studios narrowed to", count: (scope.preview_include_studios ?? []).length },
    { label: "Plex collections narrowed to (movies only)", count: (scope.preview_include_collections ?? []).length },
  ];
}

function yearSummary(scope: PrunerScopeSummary): string {
  if (scope.preview_year_min == null && scope.preview_year_max == null) return "Release years: any";
  return `Release years: ${scope.preview_year_min ?? "—"}–${scope.preview_year_max ?? "—"} (inclusive)`;
}

function ScopeWorkspaceCard({
  instanceId,
  provider,
  scope,
}: {
  instanceId: number;
  provider: string;
  scope: PrunerScopeSummary;
}) {
  const toTab = scope.media_scope === "tv" ? "tv" : "movies";
  const unsupported =
    provider === "plex" ? plexUnsupportedRuleFamilies(scope.media_scope as "tv" | "movies") : [];

  return (
    <div
      className="flex h-full flex-col gap-3 rounded-md border border-[var(--mm-border)] bg-[var(--mm-card-bg)] p-4 text-sm"
      data-testid={`pruner-overview-scope-${scope.media_scope}`}
    >
      <div className="flex flex-wrap items-start justify-between gap-2">
        <h3 className="text-sm font-semibold text-[var(--mm-text1)]">{scopeHeading(scope.media_scope)}</h3>
        <Link
          to={`/app/pruner/instances/${instanceId}/${toTab}`}
          className="text-xs font-semibold text-[var(--mm-accent)] underline-offset-2 hover:underline"
        >
          Open tab
        </Link>
      </div>

      <div>
        <p className="text-xs font-medium uppercase tracking-wide text-[var(--mm-text3)]">Rules turned on</p>
        <ul className="mt-1 list-inside list-disc space-y-0.5 text-[var(--mm-text2)]">
          {activeRuleLines(scope, provider).map((line) => (
            <li key={line}>{line}</li>
          ))}
        </ul>
      </div>

      <div>
        <p className="text-xs font-medium uppercase tracking-wide text-[var(--mm-text3)]">Optional scan filters</p>
        <p className="mt-1 text-xs text-[var(--mm-text2)]">
          These limits only change what shows up in a scan list — they do not change which saved list a delete run uses.
        </p>
        <ul className="mt-1 space-y-0.5 text-xs text-[var(--mm-text2)]">
          {filterCountSummary(scope).map((row) => (
            <li key={row.label}>
              <span className="text-[var(--mm-text3)]">{row.label}:</span>{" "}
              <span className="font-medium text-[var(--mm-text1)]">{row.count}</span>
            </li>
          ))}
          <li>
            <span className="text-[var(--mm-text3)]">{yearSummary(scope)}</span>
          </li>
        </ul>
      </div>

      <div>
        <p className="text-xs font-medium uppercase tracking-wide text-[var(--mm-text3)]">Last library scan (this tab)</p>
        <p className="mt-1 text-xs text-[var(--mm-text2)]">
          <span className="text-[var(--mm-text3)]">When:</span>{" "}
          <span className="font-medium text-[var(--mm-text1)]">{formatPrunerDateTime(scope.last_preview_at)}</span>
        </p>
        <p className="text-xs text-[var(--mm-text2)]">
          <span className="text-[var(--mm-text3)]">Result:</span>{" "}
          <span className="font-medium text-[var(--mm-text1)]">{scope.last_preview_outcome ?? "—"}</span>
          {scope.last_preview_candidate_count != null ? (
            <>
              {" "}
              · <span className="text-[var(--mm-text3)]">Items matched:</span>{" "}
              <span className="font-medium text-[var(--mm-text1)]">{scope.last_preview_candidate_count}</span>
            </>
          ) : null}
        </p>
        {scope.last_preview_error ? (
          <p className="mt-1 text-xs text-red-600" role="status">
            {scope.last_preview_error}
          </p>
        ) : null}
      </div>

      {unsupported.length ? (
        <div className="rounded-md border border-amber-900/40 bg-amber-950/20 px-2 py-2 text-xs text-amber-100/95">
          <p className="font-semibold text-amber-50">Not available on Plex for this tab</p>
          <ul className="mt-1 list-inside list-disc">
            {unsupported.map((u) => (
              <li key={u}>{u}</li>
            ))}
          </ul>
          <p className="mt-1 text-[var(--mm-text3)]">Running those scans on Plex returns a clear “not supported” result.</p>
        </div>
      ) : null}

      <p className="text-xs text-[var(--mm-text3)]">
        Max titles per manual scan on this tab:{" "}
        <span className="font-medium text-[var(--mm-text1)]">{scope.preview_max_items}</span>
      </p>
    </div>
  );
}

export function PrunerInstanceOverviewTab(props: { contextOverride?: Ctx }) {
  const outletCtx = useOutletContext<Ctx>();
  const { instanceId, instance } = props.contextOverride ?? outletCtx;

  if (!instance) {
    return null;
  }

  const tv = instance.scopes.find((s) => s.media_scope === "tv");
  const movies = instance.scopes.find((s) => s.media_scope === "movies");

  return (
    <div className="w-full min-w-0 space-y-6" data-testid="pruner-instance-overview">
      <header className="mm-page__intro !mb-0 border-0 p-0 shadow-none">
        <p className="mm-page__eyebrow">This server</p>
        <h2 className="mm-page__title text-xl sm:text-2xl">Overview</h2>
        <p className="mm-page__subtitle max-w-3xl">
          One <strong className="text-[var(--mm-text)]">{instance.provider}</strong> library connection. TV shows and
          movies are separate tabs under this server. Each scan saves a list you can review; deleting uses{" "}
          <strong className="text-[var(--mm-text)]">only</strong> the list you confirm, not a new scan.
        </p>
      </header>

      <section
        className="mm-card mm-dash-card mm-fetcher-module-surface border border-[var(--mm-border)] bg-[var(--mm-card-bg)] p-4 sm:p-5"
        aria-labelledby="pruner-overview-at-a-glance"
      >
        <h3 id="pruner-overview-at-a-glance" className="text-sm font-semibold text-[var(--mm-text1)]">
          At a glance
        </h3>
        <div className="mt-3 grid gap-4 md:grid-cols-2">
          {tv ? <ScopeWorkspaceCard instanceId={instanceId} provider={instance.provider} scope={tv} /> : null}
          {movies ? <ScopeWorkspaceCard instanceId={instanceId} provider={instance.provider} scope={movies} /> : null}
        </div>
      </section>

      <section
        className="rounded-md border border-[var(--mm-border)] bg-[var(--mm-surface2)]/40 px-4 py-3 text-sm text-[var(--mm-text2)]"
        aria-labelledby="pruner-overview-apply-note"
      >
        <h3 id="pruner-overview-apply-note" className="text-sm font-semibold text-[var(--mm-text)]">
          After you delete
        </h3>
        <p className="mt-1 text-xs sm:text-sm">
          Removed, skipped, and failed counts for each delete run are written to{" "}
          <strong className="text-[var(--mm-text)]">Activity</strong> when the job finishes. This overview does not yet
          repeat those counts from the server API.
        </p>
        <p className="mt-2">
          <Link
            className="font-semibold text-[var(--mm-accent)] underline-offset-2 hover:underline"
            to="/app/activity"
          >
            Open Activity
          </Link>
        </p>
      </section>
    </div>
  );
}
