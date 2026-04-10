import { Link } from "react-router-dom";

/** Refiner module home — movies/TV refinement; minimal pointer elsewhere for unrelated tools. */
export function RefinerPage() {
  return (
    <div className="mm-page" data-testid="refiner-scope-page">
      <header className="mm-page__intro">
        <p className="mm-page__eyebrow">MediaMop</p>
        <h1 className="mm-page__title">Refiner</h1>
        <p className="mm-page__subtitle">
          Refiner is for polishing movies and TV in your library: metadata, naming, and how files are organized. It is
          meant to stay useful no matter which download or library apps you pair with MediaMop. Later, Refiner will also
          help you clean up stale files left on disk after you are done importing — separate from anything that happens
          inside Radarr or Sonarr queues.
        </p>
        <p className="mm-page__lead text-sm text-[var(--mm-text3)]">
          <Link to="/app/fetcher" className="text-[var(--mm-accent)] underline-offset-2 hover:underline">
            Fetcher
          </Link>{" "}
          covers download queues and related checks.
        </p>
      </header>
    </div>
  );
}
