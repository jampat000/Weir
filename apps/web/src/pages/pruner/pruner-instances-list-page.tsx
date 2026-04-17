import { Link } from "react-router-dom";
import { usePrunerInstancesQuery } from "../../lib/pruner/queries";

export function PrunerInstancesListPage() {
  const q = usePrunerInstancesQuery();

  return (
    <div className="mm-page w-full min-w-0" data-testid="pruner-scope-page">
      <header className="mm-page__intro !mb-0">
        <p className="mm-page__eyebrow">MediaMop</p>
        <h1 className="mm-page__title">Pruner</h1>
        <p className="mm-page__subtitle max-w-3xl">
          Provider-first cleanup workspace for <strong className="text-[var(--mm-text)]">Emby</strong>,{" "}
          <strong className="text-[var(--mm-text)]">Jellyfin</strong>, and{" "}
          <strong className="text-[var(--mm-text)]">Plex</strong>. Configuration ownership is still instance-first:
          each registered provider server has its own <strong className="text-[var(--mm-text)]">Overview</strong>,{" "}
          <strong className="text-[var(--mm-text)]">Movies</strong>, <strong className="text-[var(--mm-text)]">TV</strong>, and{" "}
          <strong className="text-[var(--mm-text)]">Connection</strong> workspace.
        </p>
      </header>

      <section
        className="mt-6 rounded-md border border-[var(--mm-border)] bg-[var(--mm-card-bg)] px-4 py-4 text-sm text-[var(--mm-text2)]"
        aria-labelledby="pruner-framing-heading"
        data-testid="pruner-provider-framing"
      >
        <h2 id="pruner-framing-heading" className="text-base font-semibold text-[var(--mm-text1)]">
          What Pruner does for Emby, Jellyfin, and Plex
        </h2>
        <ul className="mt-2 list-inside list-disc space-y-1">
          <li>Queue provider-native previews for rule families, inspect candidate JSON, then apply from one frozen snapshot.</li>
          <li>Keep provider and instance truth separate: no shared cross-provider settings surface.</li>
          <li>
            Keep scope truth separate: <strong className="text-[var(--mm-text)]">Movies</strong> and{" "}
            <strong className="text-[var(--mm-text)]">TV</strong> are tabs inside the selected instance only.
          </li>
          <li>Provider-specific unsupported behavior remains explicit in preview outcomes and scope-tab callouts.</li>
        </ul>
      </section>

      <section className="mt-6 max-w-4xl" aria-labelledby="pruner-instances-heading">
        <h2 id="pruner-instances-heading" className="text-base font-semibold text-[var(--mm-text1)]">
          Provider instances
        </h2>
        {q.isLoading ? <p className="mt-2 text-sm text-[var(--mm-text2)]">Loading…</p> : null}
        {q.isError ? (
          <p className="mt-2 text-sm text-red-600" role="alert">
            {(q.error as Error).message}
          </p>
        ) : null}
        {q.data && q.data.length === 0 ? (
          <div
            className="mt-3 space-y-3 rounded-md border border-dashed border-[var(--mm-border)] bg-[var(--mm-surface2)]/35 px-4 py-4"
            data-testid="pruner-empty-state"
          >
            <p className="text-sm font-semibold text-[var(--mm-text1)]">No Emby, Jellyfin, or Plex instances registered yet.</p>
            <p className="text-sm text-[var(--mm-text2)]">
              Next step: register one provider server, then open its workspace to configure Overview, Movies, TV, and
              Connection for that instance.
            </p>
            <ul className="list-inside list-disc space-y-1 text-xs text-[var(--mm-text2)] sm:text-sm">
              <li>Each instance keeps credentials, rule toggles, filters, previews, and apply outcomes separate.</li>
              <li>Nothing is shared across providers or across different server rows.</li>
              <li>After previews or apply jobs finish, review outcomes in Activity.</li>
            </ul>
          </div>
        ) : null}
        {q.data && q.data.length > 0 ? (
          <ul
            className="mt-3 divide-y divide-[var(--mm-border)] rounded-md border border-[var(--mm-border)] bg-[var(--mm-card-bg)]"
            data-testid="pruner-instances-list"
          >
            {q.data.map((row) => (
              <li key={row.id} className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
                <div className="min-w-0">
                  <div className="font-medium text-[var(--mm-text1)]">{row.display_name}</div>
                  <div className="text-xs text-[var(--mm-text2)]">
                    <span className="font-medium capitalize text-[var(--mm-text)]">{row.provider}</span>
                    <span className="text-[var(--mm-text3)]"> · </span>
                    <span className="break-all font-mono text-[0.85em]">{row.base_url}</span>
                  </div>
                </div>
                <Link
                  to={`/app/pruner/instances/${row.id}/overview`}
                  className="shrink-0 rounded-md border border-[var(--mm-border)] bg-[var(--mm-surface2)] px-3 py-1.5 text-sm font-medium text-[var(--mm-text)] underline-offset-2 hover:bg-[var(--mm-card-bg)] hover:underline"
                >
                  Open workspace
                </Link>
              </li>
            ))}
          </ul>
        ) : null}
      </section>

      <p className="mt-6 max-w-3xl text-xs text-[var(--mm-text2)]">
        This page is for provider framing and instance selection; detailed history remains in each instance workspace.
        Finished previews and apply jobs also appear on{" "}
        <Link className="font-semibold text-[var(--mm-accent)] underline-offset-2 hover:underline" to="/app/activity">
          Activity
        </Link>
        . Provider-specific limits (for example Plex missing-primary caps) stay visible on the scope tabs where they apply.
      </p>
    </div>
  );
}
