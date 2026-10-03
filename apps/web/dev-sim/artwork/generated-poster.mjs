/** A poster the simulation draws itself, for a title the metadata service has none for or when it cannot be reached. */

const WIDTH = 342;
const HEIGHT = 513;
const HUE_DEGREES = 360;
const LINE_CHARACTERS = 14;
const LINE_HEIGHT = 40;
const TITLE_TOP = 250;
const MAX_LINES = 5;

/** A hue taken from the title, so a title always gets the same colours. @param {string} text */
function hueOf(text) {
  let hash = 0;
  for (const character of text) {
    hash = (hash * 31 + (character.codePointAt(0) ?? 0)) >>> 0;
  }
  return hash % HUE_DEGREES;
}

/** The title broken into lines no longer than a poster has room for. @param {string} title */
function linesOf(title) {
  const lines = [];
  for (const word of title.split(/\s+/)) {
    const last = lines.at(-1);
    if (last !== undefined && `${last} ${word}`.length <= LINE_CHARACTERS) {
      lines[lines.length - 1] = `${last} ${word}`;
    } else {
      lines.push(word);
    }
  }
  return lines.slice(0, MAX_LINES);
}

const escapeXml = (text) =>
  text.replace(/[&<>"]/g, (character) => `&#${character.charCodeAt(0)};`);

/**
 * @param {{ title: string, year: number }} title
 * @returns {{ contentType: string, body: string }}
 */
export function generatedPoster({ title, year }) {
  const hue = hueOf(title);
  const lines = linesOf(title)
    .map(
      (line, index) =>
        `<text x="${WIDTH / 2}" y="${TITLE_TOP + index * LINE_HEIGHT}" text-anchor="middle" font-size="32" font-weight="700">${escapeXml(line)}</text>`,
    )
    .join("");
  const body = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${WIDTH} ${HEIGHT}" font-family="Georgia, serif" fill="#f4f1ea"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="hsl(${hue} 45% 34%)"/><stop offset="1" stop-color="hsl(${(hue + 40) % HUE_DEGREES} 50% 14%)"/></linearGradient></defs><rect width="${WIDTH}" height="${HEIGHT}" fill="url(#g)"/>${lines}<text x="${WIDTH / 2}" y="${HEIGHT - 36}" text-anchor="middle" font-size="22" opacity="0.7">${year}</text></svg>`;
  return { contentType: "image/svg+xml", body };
}
