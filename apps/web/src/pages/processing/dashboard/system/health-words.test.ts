import { describe, expect, it } from "vitest";

import { problemWords } from "./health-words";

describe("a problem in a few words", () => {
  it("says a connection that does not answer, with what to check", () => {
    expect(
      problemWords(
        "Weir could not reach Radarr (4K) at http://localhost:7879. Check the address is right, and that the app is running and reachable from this machine.",
      ),
    ).toBe("Radarr (4K) not answering · check address");
  });

  it("says which folder is missing and to create it", () => {
    expect(
      problemWords(
        "The output folder D:/Movies does not exist. Create it, or point this workflow at a folder that does.",
      ),
    ).toBe("Output folder missing · create it");
    expect(
      problemWords(
        "The work folder D:/Work does not exist. Create it, or point this workflow's work folder at one that does.",
      ),
    ).toBe("Work folder missing · create it");
  });

  it("says which folder Weir cannot read or write, and to check permissions", () => {
    expect(
      problemWords(
        "Weir cannot read the watched folder D:/In. Check its permissions, or point this workflow at a folder Weir can read.",
      ),
    ).toBe("Can't read watched folder · check permissions");
    expect(
      problemWords(
        "Weir cannot write to the output folder D:/Out. Check its permissions, or point this workflow at a folder Weir can write to.",
      ),
    ).toBe("Can't write output folder · check permissions");
  });

  it("says a manager with no download client", () => {
    expect(
      problemWords(
        "Radarr has no enabled download client, so it has no downloads to import.",
      ),
    ).toBe("Radarr has no download client");
  });

  it("keeps the first sentence of one it does not know, without its full stop", () => {
    expect(
      problemWords("Radarr (4K) does not say where it keeps files. Open it."),
    ).toBe("Radarr (4K) does not say where it keeps files");
  });
});
