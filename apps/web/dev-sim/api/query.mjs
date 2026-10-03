/** Reading the query string of a list request. */

/** A whole-number parameter, or null when it is absent or not a number. @param {URLSearchParams} query @param {string} name */
export function intParam(query, name) {
  const raw = query.get(name);
  if (raw === null || raw === "") return null;
  const value = Number(raw);
  return Number.isFinite(value) ? Math.trunc(value) : null;
}

/** A parameter that may be given several times or as one comma-separated value. @param {URLSearchParams} query @param {string} name */
export function listParam(query, name) {
  return query
    .getAll(name)
    .flatMap((value) => value.split(","))
    .map((value) => value.trim())
    .filter(Boolean);
}

/** Whether `text` contains `needle`, ignoring case; an empty needle matches everything. */
export const contains = (text, needle) =>
  !needle || text.toLowerCase().includes(needle.toLowerCase());
