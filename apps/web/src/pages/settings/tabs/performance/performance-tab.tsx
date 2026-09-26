import { DirectPlaySection } from "./direct-play-section";
import { KeptFilesSection } from "./kept-files-section";
import { ProcessSettingsSection } from "./process-settings-section";

/**
 * Settings › Performance: how hard Weir works, which files it is deliberately leaving alone (#785), then which
 * devices should play its output directly.
 */
export function PerformanceTab() {
  return (
    <div className="mm-quiet-stack">
      <ProcessSettingsSection />
      <KeptFilesSection />
      <DirectPlaySection />
    </div>
  );
}
