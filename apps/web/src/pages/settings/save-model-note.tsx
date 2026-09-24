/**
 * How a Settings panel saves: every change on its own the moment it is made, or nothing until Save.
 * Each panel says which at its top, so nobody has to guess whether leaving loses anything.
 */
export type SaveModel = "instant" | "explicit";

export const SAVE_MODEL_WORDS: Record<SaveModel, string> = {
  instant: "Changes save as soon as you make them.",
  explicit: "Nothing changes until you press Save.",
};

export function SaveModelNote({ model }: { model: SaveModel }) {
  return (
    <p className="mm-save-model" data-testid="settings-save-model">
      {SAVE_MODEL_WORDS[model]}
    </p>
  );
}
