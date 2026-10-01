import { Panel } from "../../../../components/panels/panel";

const TMDB_URL = "https://www.themoviedb.org";

/** System › About: where the posters and title details come from, worded as TMDb requires. */
export function ArtworkCredits() {
  return (
    <Panel
      title="Posters and title details"
      headingId="about-artwork-heading"
      padded
      dataTestId="about-artwork-credits"
    >
      <p className="mm-quiet-note">
        This product uses the TMDB API but is not endorsed or certified by TMDB.
      </p>
      <p className="mt-4">
        <a
          href={TMDB_URL}
          target="_blank"
          rel="noreferrer"
          className="mm-quiet-link"
          data-testid="about-tmdb-link"
        >
          themoviedb.org →
        </a>
      </p>
    </Panel>
  );
}
