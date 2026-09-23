import { useMemo, useState } from "react";

import type { RuleSetBinding } from "./rule-set-fields";
import {
  DEFAULT_AUDIO_SORTERS,
  DEFAULT_SUBTITLE_SORTERS,
  dumpSorters,
  parseSorters,
} from "./rule-set-model";
import { SorterEditor } from "./sorter-editor";

/** The ordered criteria behind the defaults, closed until asked for. */
export function TrackOrderSection({ binding }: { binding: RuleSetBinding }) {
  const [open, setOpen] = useState(false);
  const { draft, change, disabled } = binding;
  const audioSorters = useMemo(
    () => parseSorters(draft.audio_sorters_json, DEFAULT_AUDIO_SORTERS),
    [draft.audio_sorters_json],
  );
  const subtitleSorters = useMemo(
    () => parseSorters(draft.subtitle_sorters_json, DEFAULT_SUBTITLE_SORTERS),
    [draft.subtitle_sorters_json],
  );

  return (
    <section>
      <button
        type="button"
        className="mm-track-order__toggle"
        aria-expanded={open}
        onClick={() => setOpen((current) => !current)}
      >
        <span>
          <span className="mm-track-order__title">Advanced track ordering</span>
          <span className="mm-field__hint">
            Override the profile defaults with ordered codec, channel, bitrate,
            and flag criteria.
          </span>
        </span>
        <span className="mm-track-order__action">{open ? "Hide" : "Edit"}</span>
      </button>
      {open ? (
        <div className="mt-8 space-y-10">
          <SorterEditor
            title="Audio order"
            detail="The first matching criterion wins. Only add a match value when a criterion needs one."
            rows={audioSorters}
            disabled={disabled}
            onChange={(rows) => change("audio_sorters_json", dumpSorters(rows))}
          />
          <SorterEditor
            title="Subtitle order"
            detail="These criteria rank only the subtitle tracks retained above."
            rows={subtitleSorters}
            disabled={disabled}
            onChange={(rows) =>
              change("subtitle_sorters_json", dumpSorters(rows))
            }
          />
        </div>
      ) : null}
    </section>
  );
}
