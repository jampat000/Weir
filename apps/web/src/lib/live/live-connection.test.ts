import { renderHook } from "@testing-library/react";
import { act } from "react";
import { afterEach, describe, expect, it } from "vitest";

import {
  FIRST_CONNECTION,
  nextLiveConnection,
  reportLiveConnection,
  resetLiveConnection,
  useLiveConnection,
} from "./live-connection";

afterEach(() => resetLiveConnection());

describe("nextLiveConnection", () => {
  it("is live, and has been, once the stream opens", () => {
    expect(nextLiveConnection(FIRST_CONNECTION, "opened")).toEqual({
      status: "live",
      hasBeenLive: true,
    });
  });

  it("is lost after a drop, remembering whether it was ever up", () => {
    const live = nextLiveConnection(FIRST_CONNECTION, "opened");

    expect(nextLiveConnection(live, "dropped")).toEqual({
      status: "lost",
      hasBeenLive: true,
    });
    expect(nextLiveConnection(FIRST_CONNECTION, "dropped")).toEqual({
      status: "lost",
      hasBeenLive: false,
    });
  });
});

describe("the shared connection", () => {
  it("says it reconnected only when it opens after being lost", () => {
    expect(reportLiveConnection("opened")).toEqual({ reconnected: false });
    expect(reportLiveConnection("dropped")).toEqual({ reconnected: false });
    expect(reportLiveConnection("opened")).toEqual({ reconnected: true });
    expect(reportLiveConnection("opened")).toEqual({ reconnected: false });
  });

  it("follows the stream and starts over once nothing watches it", () => {
    const { result } = renderHook(() => useLiveConnection());
    expect(result.current.status).toBe("connecting");

    act(() => void reportLiveConnection("opened"));
    expect(result.current.status).toBe("live");

    act(() => void reportLiveConnection("dropped"));
    expect(result.current).toEqual({ status: "lost", hasBeenLive: true });

    act(() => resetLiveConnection());
    expect(result.current).toEqual(FIRST_CONNECTION);
  });
});
