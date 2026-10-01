import { Panel } from "../../../../components/panels/panel";

const TMDB_URL = "https://www.themoviedb.org";
const TVDB_URL = "https://thetvdb.com";

/** System › About: where the posters and title details come from: TMDb's credit in the words it asks for, and TheTVDB's with a link, both always shown. */
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
      <p className="mm-quiet-note mt-4">
        TV information and images are provided by TheTVDB.com, but we are not
        endorsed or certified by TheTVDB.com or its affiliates.
      </p>
      <p className="mt-4">
        <a
          href={TVDB_URL}
          target="_blank"
          rel="noreferrer"
          className="mm-quiet-link"
          data-testid="about-tvdb-link"
        >
          thetvdb.com →
        </a>
      </p>
    </Panel>
  );
}
