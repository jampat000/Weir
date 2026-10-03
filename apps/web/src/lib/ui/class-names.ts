/** Joins the class names that are set: `classNames("a", false, "b")` is "a b". */
export function classNames(
  ...names: ReadonlyArray<string | false | null | undefined>
): string {
  return names.filter(Boolean).join(" ");
}
