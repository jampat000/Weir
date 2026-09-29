import { describe, expect, it } from "vitest";

// A password input with no `autoComplete` lets the browser treat the whole form as a Weir sign-in, so the saved
// Weir username and password are filled into another service's address and key fields (#836).
const sources = import.meta.glob<string>(["../**/*.tsx", "!../**/*.test.tsx"], {
  query: "?raw",
  import: "default",
  eager: true,
});

const INPUT_OPENING = "<input";
const PASSWORD_TYPE = /\btype=(?:"password"|\{[^}]*"password"[^}]*\})/;
const AUTOCOMPLETE_ATTRIBUTE = /\bautoComplete=/;

/** The text of each `<input ...>` opening tag; braces are tracked so an arrow's `>` does not end a tag early. */
function inputTags(source: string): string[] {
  const tags: string[] = [];
  let start = source.indexOf(INPUT_OPENING);
  while (start !== -1) {
    let depth = 0;
    let end = start + INPUT_OPENING.length;
    for (; end < source.length; end++) {
      const character = source[end];
      if (character === "{") depth++;
      else if (character === "}") depth--;
      else if (character === ">" && depth === 0) break;
    }
    tags.push(source.slice(start, end + 1));
    start = source.indexOf(INPUT_OPENING, end);
  }
  return tags;
}

function passwordInputsWithoutAutoComplete(source: string): string[] {
  return inputTags(source).filter(
    (tag) => PASSWORD_TYPE.test(tag) && !AUTOCOMPLETE_ATTRIBUTE.test(tag),
  );
}

describe("password inputs", () => {
  it("every password input in the app says what it is with autoComplete", () => {
    const offenders = Object.entries(sources)
      .filter(
        ([, source]) => passwordInputsWithoutAutoComplete(source).length > 0,
      )
      .map(([file]) => file);

    expect(offenders).toEqual([]);
  });

  it("the scan finds the password inputs it is meant to guard", () => {
    const passwordInputCount = Object.values(sources)
      .flatMap(inputTags)
      .filter((tag) => PASSWORD_TYPE.test(tag)).length;

    expect(passwordInputCount).toBeGreaterThan(0);
  });

  it("a password input without autoComplete is reported", () => {
    const source = `<input\n  type="password"\n  onChange={(e) => go(e.target.value)}\n/>`;

    expect(passwordInputsWithoutAutoComplete(source)).toHaveLength(1);
  });

  it("a password input whose type is chosen in an expression is reported", () => {
    const source = `<input type={shown ? "text" : "password"} className="x" />`;

    expect(passwordInputsWithoutAutoComplete(source)).toHaveLength(1);
  });

  it("a password input with autoComplete after an arrow function is accepted", () => {
    const source = `<input type="password" onChange={(e) => go(e)} autoComplete="new-password" />`;

    expect(passwordInputsWithoutAutoComplete(source)).toEqual([]);
  });

  it("a text input without autoComplete is not reported", () => {
    const source = `<input type="text" className="x" />`;

    expect(passwordInputsWithoutAutoComplete(source)).toEqual([]);
  });
});
