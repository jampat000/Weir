import type { AppSettings } from "../../../../lib/settings/types";
import { SHOW_SUPPORT_CARD } from "../../../../lib/support";
import { AboutFacts } from "./about-facts";
import { SetupWizardSection } from "./setup-wizard-section";
import { SupportSection } from "./support-section";
import { UpdateSection } from "./update-section";

/** System › About: what this Weir is before anything you can change about it. */
export function AboutTab({ settings }: { settings: AppSettings }) {
  return (
    <div className="mm-about-grid">
      <AboutFacts />
      <UpdateSection />
      <SetupWizardSection settings={settings} />
      {SHOW_SUPPORT_CARD ? <SupportSection /> : null}
    </div>
  );
}
