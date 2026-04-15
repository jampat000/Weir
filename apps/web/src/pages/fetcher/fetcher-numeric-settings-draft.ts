/** Helpers for Fetcher settings numeric inputs that allow a blank string while editing (run interval, retry, search limit, etc.). */

export function trimDraft(raw: string): string {
  return raw.trim();
}

export function draftDiffersFromCommittedLabel(draft: string | null, committedLabel: string): boolean {
  if (draft === null) {
    return false;
  }
  return trimDraft(draft) !== trimDraft(committedLabel);
}
