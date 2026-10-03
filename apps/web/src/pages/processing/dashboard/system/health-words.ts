/**
 * The few words a Health row says for a problem the server wrote as a sentence, as the Dashboard says everything
 * else: what is wrong, then the one thing to do, with a dot between. The sentence itself is the row's tooltip and, for
 * a workflow, the Details drawer's text, so nothing is lost. A sentence none of these recognise is cut to its first
 * sentence, which the row's one line shortens with an ellipsis if it must.
 */

const capital = (word: string) => word.charAt(0).toUpperCase() + word.slice(1);

type Rule = [pattern: RegExp, words: (match: RegExpExecArray) => string];

const RULES: readonly Rule[] = [
  [
    /^Weir could not reach (.+?) at \S+?\.(?:\s|$)/,
    (match) => `${match[1]} not answering · check address`,
  ],
  [
    /^The (\w+) folder .+? does not exist\./,
    (match) => `${capital(match[1])} folder missing · create it`,
  ],
  [
    /^Weir cannot read the (\w+) folder /,
    (match) => `Can't read ${match[1]} folder · check permissions`,
  ],
  [
    /^Weir cannot write to the (\w+) folder /,
    (match) => `Can't write ${match[1]} folder · check permissions`,
  ],
  [
    /^(.+?) has no enabled download client/,
    (match) => `${match[1]} has no download client`,
  ],
];

/** The first sentence of `text`, without its full stop. */
function firstSentence(text: string): string {
  const [first] = text.trim().split(/(?<=[.!?])\s+/);
  return first.replace(/\.$/, "");
}

/** What a problem sentence says in a few words: "Radarr (4K) not answering · check address". */
export function problemWords(sentence: string): string {
  const text = sentence.trim();
  for (const [pattern, words] of RULES) {
    const match = pattern.exec(text);
    if (match) return words(match);
  }
  return firstSentence(text);
}
