import { LICENSE_URL, REPOSITORY_URL } from "../../../../lib/repository";
import { ArtworkCredits } from "./artwork-credits";
import { SupportSection } from "./support-section";

/** System › About's last row: who Weir's posters come from, the licence and the source, and where to support it. */
export function AboutFooter() {
  return (
    <footer className="mm-about-footer">
      <ArtworkCredits />
      <div className="mm-about-footer__links">
        <a
          href={LICENSE_URL}
          target="_blank"
          rel="noreferrer"
          className="mm-sys-link"
        >
          AGPL-3.0-or-later
        </a>
        <a
          href={REPOSITORY_URL}
          target="_blank"
          rel="noreferrer"
          className="mm-sys-link"
          data-testid="about-source-code-link"
        >
          Source on GitHub
        </a>
        <SupportSection />
      </div>
    </footer>
  );
}
