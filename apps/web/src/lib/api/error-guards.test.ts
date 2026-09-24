import { describe, expect, it } from "vitest";
import { ApiHttpError } from "./client";
import {
  httpStatusFromApiError,
  isHttpErrorFromApi,
  isLikelyNetworkFailure,
  isLikelyViteProxyUpstreamDown,
} from "./error-guards";

const BOOTSTRAP = "/api/v1/auth/bootstrap";

describe("error-guards", () => {
  it("treats TypeError as network", () => {
    expect(isLikelyNetworkFailure(new TypeError("Failed to fetch"))).toBe(true);
  });

  it("treats Failed to fetch as network", () => {
    expect(isLikelyNetworkFailure(new Error("Failed to fetch"))).toBe(true);
  });

  it("does not treat an answer from the API as network", () => {
    expect(
      isLikelyNetworkFailure(new ApiHttpError(BOOTSTRAP, 503, "Not ready")),
    ).toBe(false);
  });

  it("treats an ApiHttpError marked networkUnreachable as network, regardless of its message", () => {
    const error = new ApiHttpError(
      BOOTSTRAP,
      0,
      "Can't reach Weir. Check it's still running, then try again.",
      undefined,
      false,
      true,
    );
    expect(isLikelyNetworkFailure(error)).toBe(true);
  });

  it("does not treat a timeout ApiHttpError as network-unreachable", () => {
    const error = new ApiHttpError(
      BOOTSTRAP,
      0,
      "Request timed out - the backend may be slow or unreachable.",
      undefined,
      true,
    );
    expect(isLikelyNetworkFailure(error)).toBe(false);
  });

  it("detects HTTP errors from the API, and nothing else", () => {
    expect(
      isHttpErrorFromApi(
        new ApiHttpError("/api/v1/auth/me", 401, "Signed out"),
      ),
    ).toBe(true);
    expect(isHttpErrorFromApi(new Error("me: 401"))).toBe(false);
  });

  it("reads the status from an API error", () => {
    expect(
      httpStatusFromApiError(
        new ApiHttpError("/api/v1/auth/me", 401, "Signed out"),
      ),
    ).toBe(401);
    expect(httpStatusFromApiError(new Error("nope"))).toBe(null);
  });

  it("treats HTTP 500 in Vite dev as proxy upstream down", () => {
    expect(
      isLikelyViteProxyUpstreamDown(new ApiHttpError(BOOTSTRAP, 500, "")),
    ).toBe(Boolean(import.meta.env.DEV));
  });

  it("does not treat other statuses as Vite proxy upstream down", () => {
    expect(
      isLikelyViteProxyUpstreamDown(new ApiHttpError(BOOTSTRAP, 503, "")),
    ).toBe(false);
  });
});
