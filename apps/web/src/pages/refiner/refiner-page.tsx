import { Link } from "react-router-dom";

/**
 * Refiner module overview. Queue-driven *arr failed-import tools live under Fetcher (`/app/fetcher`).
 */
export function RefinerPage() {
  return (
    <div className="mm-page" data-testid="refiner-scope-page">
      <header className="mm-page__intro">
        <p className="mm-page__eyebrow">MediaMop</p>
        <h1 className="mm-page__title">Refiner</h1>
        <p className="mm-page__subtitle">
          Refiner is MediaMop’s module for refining movies and TV: structure and quality of metadata and media files,
          without tying the product to one downloader or indexer stack. A separate lane of work —{" "}
          <strong>Refiner cleanup</strong> — will mean stale files left on disk after importing and processing are done;
          that is not the same as walking Radarr/Sonarr download queues.
        </p>
        <p className="mm-page__lead">
          This page is the module home today. Radarr/Sonarr <strong>download-queue</strong> failed-import review,
          schedules, and manual actions live under{" "}
          <Link to="/app/fetcher" className="text-[var(--mm-accent)] underline-offset-2 hover:underline">
            Fetcher
          </Link>{" "}
          because MediaMop treats that workflow as Fetcher-owned surface area.
        </p>
      </header>
    </div>
  );
}
