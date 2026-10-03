/** What the header's pill says while Weir is paused: how the pause ends, in the fullest words down to one. */

/**
 * The wordings, the fullest first. A pause with an end says when ("Paused · until 10 pm"); one with none says it
 * lasts until someone resumes ("Paused · manually"). Where nothing else fits it says "Paused".
 */
export function pausePillWords(
  until: { full: string; clock: string } | null,
): string[] {
  return until
    ? [
        `Paused · until ${until.full}`,
        `Paused · until ${until.clock}`,
        "Paused",
      ]
    : ["Paused · until you resume", "Paused · manually", "Paused"];
}
