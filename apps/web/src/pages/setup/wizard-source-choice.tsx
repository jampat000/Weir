import { DOWNLOAD_SOURCE_OPTIONS, type DownloadSource } from "./wizard-source";

/** The first question of setup: how downloads reach Weir. Changing the answer keeps what was connected. */
export function WizardSourceChoice({
  value,
  disabled,
  onChange,
}: {
  value: DownloadSource | null;
  disabled: boolean;
  onChange: (source: DownloadSource) => void;
}) {
  return (
    <fieldset className="mm-wizard-choices">
      <legend className="mm-wizard-label">
        How do your downloads reach Weir?
      </legend>
      {DOWNLOAD_SOURCE_OPTIONS.map((option) => (
        <label key={option.value} className="mm-wizard-choice">
          <input
            type="radio"
            name="setup-wizard-source"
            className="mt-0.5 h-4 w-4 shrink-0 accent-mm-accent"
            value={option.value}
            checked={value === option.value}
            disabled={disabled}
            onChange={() => onChange(option.value)}
          />
          <span>{option.label}</span>
        </label>
      ))}
    </fieldset>
  );
}
