import { describe, expect, it } from "vitest";

import type { CurrentSession } from "../../../../lib/api/types";
import type { SecurityOverview } from "../../../../lib/settings/types";
import {
  currentSignInRows,
  formatSessionTimeout,
  signInProtections,
} from "./security-facts";

const session = {
  trusted_device: true,
  idle_timeout_minutes: 7 * 1440,
  absolute_timeout_days: 30,
} as CurrentSession;

const overview = {
  session_signing_configured: true,
  sign_in_cookie_https_mode: "auto",
  sign_in_cookie_https_plain: "Matched to each connection",
  sign_in_cookie_same_site: "Lax",
  standard_session_idle_timeout_plain: "1 day",
  standard_session_absolute_timeout_plain: "7 days",
  trusted_session_idle_timeout_plain: "7 days",
  trusted_session_absolute_timeout_plain: "30 days",
  extra_https_hardening_enabled: true,
  sign_in_attempt_limit: 8,
  sign_in_attempt_window_plain: "15 minutes",
  first_time_setup_attempt_limit: 5,
  first_time_setup_attempt_window_plain: "15 minutes",
  allowed_browser_origins_count: 1,
} as SecurityOverview;

describe("formatSessionTimeout", () => {
  it("uses the largest whole unit", () => {
    expect(formatSessionTimeout(2 * 1440)).toBe("2 days");
    expect(formatSessionTimeout(480)).toBe("8 hours");
    expect(formatSessionTimeout(90)).toBe("90 minutes");
  });
});

describe("currentSignInRows", () => {
  it("says in three short lines how this browser is signed in", () => {
    const rows = currentSignInRows(session, overview, false);

    expect(rows.map((row) => row.label)).toEqual([
      "This browser",
      "Signs out after",
      "Trusted devices",
    ]);
    expect(rows[0]).toMatchObject({ value: "Trusted", meaning: "done" });
    expect(rows[1].value).toBe("7 days idle · at most 30 days");
    expect(rows[2]).toMatchObject({
      value: "30 days",
      detail: "Idle timeout 7 days",
    });
  });

  it("falls back to the server's policy until the session has loaded", () => {
    const rows = currentSignInRows(undefined, overview, false);

    expect(rows[0]).toMatchObject({ value: "Loading...", meaning: "doing" });
    expect(rows[1].value).toBe("1 day idle · at most 7 days");
  });

  it("says when the session could not be read", () => {
    expect(currentSignInRows(undefined, undefined, true)[0]).toMatchObject({
      value: "Unavailable",
      meaning: "broken",
    });
  });
});

describe("signInProtections", () => {
  it("lists every protection and flags none when all are in force", () => {
    const protections = signInProtections(overview);

    expect(protections).toHaveLength(9);
    expect(protections.some((p) => p.needsAttention)).toBe(false);
  });

  it("flags the ones that are off", () => {
    const flagged = signInProtections({
      ...overview,
      extra_https_hardening_enabled: false,
      sign_in_cookie_https_mode: "never",
    })
      .filter((p) => p.needsAttention)
      .map((p) => p.label);

    expect(flagged).toEqual([
      "HTTPS-only sign-in cookie",
      "Extra HTTPS protection",
    ]);
  });
});
