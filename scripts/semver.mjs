// SemVer 2.0.0 versions as release tooling needs them: parsing a release version and ordering two of them.
// Build metadata ("+build") is not accepted: a release version becomes a Docker tag and a package file name,
// and neither can carry it.

const IDENTIFIER = String.raw`(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)`;
const RELEASE_VERSION = new RegExp(
  String.raw`^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-(${IDENTIFIER}(?:\.${IDENTIFIER})*))?$`,
);
const NUMERIC_IDENTIFIER = /^\d+$/;

/**
 * Parses "1.2.3" or "1.2.3-rc.1" (a leading "v" is allowed). Returns null for anything else.
 * `prerelease` is the list of dot-separated identifiers after the hyphen; empty for a stable release.
 */
export function parseSemver(text) {
  const trimmed = (text ?? "").trim();
  const bare = trimmed.startsWith("v") ? trimmed.slice(1) : trimmed;
  const match = RELEASE_VERSION.exec(bare);
  if (!match) return null;
  return {
    version: bare,
    major: match[1],
    minor: match[2],
    patch: match[3],
    prerelease: match[4] ? match[4].split(".") : [],
  };
}

export function isPrerelease(parsed) {
  return parsed.prerelease.length > 0;
}

// Digit strings of any length compare by value without converting them to numbers: a longer string
// without leading zeros is the bigger number.
function compareNumbers(left, right) {
  if (left.length !== right.length) return left.length < right.length ? -1 : 1;
  if (left === right) return 0;
  return left < right ? -1 : 1;
}

function compareIdentifiers(left, right) {
  const leftNumeric = NUMERIC_IDENTIFIER.test(left);
  const rightNumeric = NUMERIC_IDENTIFIER.test(right);
  if (leftNumeric && rightNumeric) return compareNumbers(left, right);
  if (leftNumeric) return -1;
  if (rightNumeric) return 1;
  if (left === right) return 0;
  return left < right ? -1 : 1;
}

function comparePrerelease(left, right) {
  if (left.length === 0 && right.length === 0) return 0;
  if (left.length === 0) return 1;
  if (right.length === 0) return -1;
  for (let index = 0; index < Math.min(left.length, right.length); index += 1) {
    const result = compareIdentifiers(left[index], right[index]);
    if (result !== 0) return result;
  }
  return Math.sign(left.length - right.length);
}

/** SemVer precedence: negative when `left` is older than `right`, zero when equal, positive when newer. */
export function compareSemver(left, right) {
  for (const part of ["major", "minor", "patch"]) {
    const result = compareNumbers(left[part], right[part]);
    if (result !== 0) return result;
  }
  return comparePrerelease(left.prerelease, right.prerelease);
}
