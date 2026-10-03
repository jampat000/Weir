import type { AppSettings } from "../../../../lib/settings/types";
import { AboutFooter } from "./about-footer";
import { MediaToolsSection } from "./media-tools-section";
import { ThisPcSection } from "./this-pc-section";
import { ThisWeirSection } from "./this-weir-section";
import { UpdateSection } from "./update-section";

/**
 * System › About: what this Weir is and what it works with, then how it updates and where it runs. Two rows of two
 * cards, each row as tall as its taller card, so the pairs line up top and bottom.
 */
export function AboutTab({
  settings,
  editable,
  onTimeZoneUnsavedChange,
}: {
  settings: AppSettings;
  editable: boolean;
  onTimeZoneUnsavedChange: (unsaved: boolean) => void;
}) {
  return (
    <div className="mm-sys-stack">
      <div className="mm-sys-grid">
        <ThisWeirSection settings={settings} />
        <MediaToolsSection />
        <UpdateSection />
        <ThisPcSection
          settings={settings}
          editable={editable}
          onTimeZoneUnsavedChange={onTimeZoneUnsavedChange}
        />
      </div>
      <AboutFooter />
    </div>
  );
}
