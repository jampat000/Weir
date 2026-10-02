import { describe, expect, it } from "vitest";

import tokens from "../../styles/weir-tokens.css?raw";
import recipe from "../../styles/weir-status.css?raw";
import { STATUS_MEANINGS } from "./status-meaning";

describe("the status meanings", () => {
  it.each(STATUS_MEANINGS)(
    "%s has a colour and a rule that draws it",
    (meaning) => {
      expect(tokens).toContain(`--mm-status-${meaning}:`);
      expect(recipe).toMatch(
        new RegExp(
          `\\[data-status="${meaning}"\\]\\s*\\{[^}]*--mm-st:\\s*var\\(--mm-status-${meaning}\\)`,
        ),
      );
    },
  );

  it("the light theme chooses its own colour for every colour that cannot be shared", () => {
    const light = tokens.slice(tokens.indexOf('html[data-mm-theme="light"]'));

    for (const token of ["done", "doing", "attention", "broken"]) {
      expect(light).toContain(`--mm-status-${token}:`);
    }
    expect(light).toContain("--mm-payoff:");
  });
});
