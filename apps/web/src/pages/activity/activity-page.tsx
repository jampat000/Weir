import { PageLoading } from "../../components/shared/page-loading";
import { useActivityRecentQuery } from "../../lib/activity/queries";
import { isHttpErrorFromApi, isLikelyNetworkFailure } from "../../lib/api/error-guards";

function formatEventTime(iso: string): string {
  try {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) {
      return iso;
    }
    return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(d);
  } catch {
    return iso;
  }
}

export function ActivityPage() {
  const recent = useActivityRecentQuery();

  if (recent.isPending) {
    return <PageLoading label="Loading activity" />;
  }

  if (recent.isError) {
    const err = recent.error;
    return (
      <div className="mm-page">
        <header className="mm-page__intro">
          <p className="mm-page__eyebrow">Overview</p>
          <h1 className="mm-page__title">Activity</h1>
          <p className="mm-page__lead">
            {isLikelyNetworkFailure(err)
              ? "Could not reach the MediaMop API."
              : isHttpErrorFromApi(err)
                ? "The server refused this request. Sign in again if needed."
                : "Could not load activity."}
          </p>
        </header>
        {err instanceof Error ? (
          <p className="mm-page__lead font-mono text-sm text-[var(--mm-text3)]">{err.message}</p>
        ) : null}
      </div>
    );
  }

  const items = recent.data.items;

  return (
    <div className="mm-page">
      <header className="mm-page__intro">
        <p className="mm-page__eyebrow">Overview</p>
        <h1 className="mm-page__title">Activity</h1>
        <p className="mm-page__subtitle">
          Persisted platform events, newest first. Snapshot only — refresh or revisit to update.
        </p>
        <p className="mm-page__lead">
          Read-only: no filters, export, or actions. Records sign-in outcomes, setup, sign-out, failed bootstrap
          attempts, and throttled Fetcher health checks — not every page load.
        </p>
      </header>

      {items.length === 0 ? (
        <section className="mm-activity-panel" aria-label="Activity entries">
          <p className="mm-activity-empty">No events recorded yet.</p>
        </section>
      ) : (
        <ul className="mm-activity-feed" aria-label="Recent activity">
          {items.map((ev) => (
            <li key={ev.id} className="mm-activity-row">
              <div className="mm-activity-row__meta">
                <span className="mm-activity-pill">{ev.module}</span>
                <time className="mm-activity-row__time" dateTime={ev.created_at}>
                  {formatEventTime(ev.created_at)}
                </time>
              </div>
              <div className="mm-activity-row__body">
                <span className="mm-activity-row__title">{ev.title}</span>
                {ev.detail ? <span className="mm-activity-row__detail">{ev.detail}</span> : null}
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
