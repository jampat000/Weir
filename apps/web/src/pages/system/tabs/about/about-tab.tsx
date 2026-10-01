import type { AppSettings } from "../../../../lib/settings/types";
import { SHOW_SUPPORT_CARD } from "../../../../lib/support";
import { AboutFacts } from "./about-facts";
import { ArtworkCredits } from "./artwork-credits";
import { NetworkAccessSection } from "./network-access-section";
import { SetupWizardSection } from "./setup-wizard-section";
import { SupportSection } from "./support-section";
import { UpdateSection } from "./update-section";

/** System › About: what this Weir is before anything you can change about it. */
export function AboutTab({ settings }: { settings: AppSettings }) {
  return (
    <div className="mm-about-grid">
      <AboutFacts />
      <NetworkAccessSection />
      <UpdateSection />
      <ArtworkCredits />
      <SetupWizardSection settings={settings} />
      {SHOW_SUPPORT_CARD ? <SupportSection /> : null}
    </div>
  );
}
