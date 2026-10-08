import { describe, expect, it } from "vitest";

import { parseServerHello } from "./server-hello";

describe("parseServerHello", () => {
  it("reads the boot id of a server.hello frame", () => {
    expect(parseServerHello('{"boot_id":"2f0c"}')).toBe("2f0c");
  });

  it("ignores anything that does not carry one", () => {
    expect(parseServerHello('{"boot_id":""}')).toBeNull();
    expect(parseServerHello('{"boot_id":7}')).toBeNull();
    expect(parseServerHello("{}")).toBeNull();
    expect(parseServerHello("nope")).toBeNull();
  });
});
