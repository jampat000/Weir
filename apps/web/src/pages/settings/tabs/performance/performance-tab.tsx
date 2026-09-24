import { DirectPlaySection } from "./direct-play-section";
import { ProcessSettingsSection } from "./process-settings-section";

/** Settings › Performance: how hard Weir works, then which devices should play its output directly. */
export function PerformanceTab() {
  return (
    <div className="mm-quiet-stack">
      <ProcessSettingsSection />
      <DirectPlaySection />
    </div>
  );
}
