import tmdbLogoUrl from "../../../../assets/tmdb-logo.svg";
import { Panel } from "../../../../components/panels/panel";

const TMDB_URL = "https://www.themoviedb.org";
const TVDB_URL = "https://thetvdb.com";

/** System › About: where the posters and title details come from: TMDb's credit in the words it asks for, and TheTVDB's in the words Deluno uses, linked to its site, both always shown. */
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
      <div className="mt-4 flex flex-wrap items-center gap-4">
        <a
          href={TMDB_URL}
          target="_blank"
          rel="noreferrer"
          data-testid="about-tmdb-logo-link"
        >
          <img src={tmdbLogoUrl} alt="TMDB" className="h-3.5 w-auto" />
        </a>
        <a
          href={TMDB_URL}
          target="_blank"
          rel="noreferrer"
          className="mm-quiet-link"
          data-testid="about-tmdb-link"
        >
          themoviedb.org →
        </a>
      </div>
      <p className="mm-quiet-note mt-4" data-testid="about-tvdb-credit">
        TV metadata provided by{" "}
        <a
          href={TVDB_URL}
          target="_blank"
          rel="noreferrer noopener"
          className="mm-quiet-link"
          data-testid="about-tvdb-link"
        >
          TheTVDB
        </a>
      </p>
    </Panel>
  );
}
