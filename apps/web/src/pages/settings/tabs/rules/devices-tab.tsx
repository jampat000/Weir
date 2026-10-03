import { PlaybackDevicesSection } from "./playback-devices-section";

/** Setup › Rules › Playback devices: which devices you play on, so each file can say whether it will Direct Play. */
export function DevicesTab() {
  return (
    <div className="mm-quiet-stack">
      <PlaybackDevicesSection />
    </div>
  );
}
