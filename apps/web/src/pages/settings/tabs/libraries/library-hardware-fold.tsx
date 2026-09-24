import { HARDWARE_DECODE_OPTIONS, STRICTNESS_OPTIONS } from "./library-options";
import {
  SelectSetting,
  TextSetting,
  type LibraryFormBinding,
} from "./library-settings";

/** Hardware decoding and FFmpeg's strictness, folded away: software is the safe default. */
export function LibraryHardwareFold({
  binding,
}: {
  binding: LibraryFormBinding;
}) {
  return (
    <details>
      <summary className="mm-library-fold__summary">
        Hardware and compatibility
      </summary>
      <p className="mm-library-fold__detail">
        Software processing is the safest default. Hardware failures fall back
        to software and are recorded.
      </p>
      <div className="mm-field-row mt-4">
        <SelectSetting
          binding={binding}
          name="hardware_decode_mode"
          label="Hardware decoding"
          options={HARDWARE_DECODE_OPTIONS}
        />
        <TextSetting
          binding={binding}
          name="hardware_device"
          label="Hardware method"
          width="medium"
          placeholder="cuda, qsv, vaapi"
        />
        <TextSetting
          binding={binding}
          name="hardware_disabled_vendors_csv"
          label="Never use these vendors"
          width="medium"
          placeholder="nvidia,intel"
        />
        <SelectSetting
          binding={binding}
          name="ffmpeg_strictness"
          label="FFmpeg compatibility"
          options={STRICTNESS_OPTIONS}
        />
      </div>
    </details>
  );
}
