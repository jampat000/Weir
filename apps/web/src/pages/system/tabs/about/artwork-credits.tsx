import tmdbLogoUrl from "../../../../assets/tmdb-logo.svg";

const TMDB_URL = "https://www.themoviedb.org";
const TVDB_URL = "https://thetvdb.com";

/** System › About: where the posters and title details come from: TMDb's credit in the words it asks for, and TheTVDB's in the words Deluno uses, linked to its site, both always shown. */
export function ArtworkCredits() {
  return (
    <div className="mm-about-credits" data-testid="about-artwork-credits">
      <a
        href={TMDB_URL}
        target="_blank"
        rel="noreferrer"
        data-testid="about-tmdb-link"
      >
        <img src={tmdbLogoUrl} alt="TMDB" className="h-3.5 w-auto" />
      </a>
      <p>
        This product uses the TMDB API but is not endorsed or certified by TMDB.
      </p>
      <p data-testid="about-tvdb-credit">
        TV metadata provided by{" "}
        <a
          href={TVDB_URL}
          target="_blank"
          rel="noreferrer noopener"
          className="mm-sys-link"
          data-testid="about-tvdb-link"
        >
          TheTVDB
        </a>
      </p>
    </div>
  );
}
