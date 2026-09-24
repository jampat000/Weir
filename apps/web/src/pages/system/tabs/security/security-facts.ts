import type { Fact } from "../../../../components/shared/fact-table";
import type { CurrentSession } from "../../../../lib/api/types";
import type { SecurityOverview } from "../../../../lib/settings/types";
import { plural } from "../../../../lib/ui/mm-plural";

const LOADING = "Loading...";
const MINUTES_PER_HOUR = 60;
const MINUTES_PER_DAY = 1440;

/** "2 days", "8 hours", "30 minutes": the largest whole unit. */
export function formatSessionTimeout(minutes: number): string {
  if (minutes % MINUTES_PER_DAY === 0) {
    return plural(minutes / MINUTES_PER_DAY, "day", "days");
  }
  if (minutes % MINUTES_PER_HOUR === 0) {
    return plural(minutes / MINUTES_PER_HOUR, "hour", "hours");
  }
  return plural(minutes, "minute", "minutes");
}

/** How this browser is signed in, falling back to the server's policy until the session has loaded. */
export function currentSignInFacts(
  session: CurrentSession | null | undefined,
  overview: SecurityOverview | undefined,
  sessionFailed: boolean,
): Fact[] {
  const trusted = session?.trusted_device ?? false;
  return [
    {
      label: "This browser",
      value: session
        ? trusted
          ? "Trusted"
          : "Standard"
        : sessionFailed
          ? "Unavailable"
          : LOADING,
      detail: session
        ? trusted
          ? "Long-lived sign-in for this device"
          : "Normal sign-in lifetime"
        : sessionFailed
          ? "Could not read the current sign-in session."
          : "Checking the current sign-in session.",
    },
    {
      label: "Idle timeout",
      value: session
        ? formatSessionTimeout(session.idle_timeout_minutes)
        : (overview?.standard_session_idle_timeout_plain ?? LOADING),
      detail: trusted ? "Trusted-device idle timeout" : "Standard idle timeout",
    },
    {
      label: "Max sign-in age",
      value: session
        ? plural(session.absolute_timeout_days, "day", "days")
        : (overview?.standard_session_absolute_timeout_plain ?? LOADING),
      detail: trusted
        ? "Trusted-device maximum session age"
        : "Standard maximum session age",
    },
    {
      label: "Trusted devices",
      value: overview?.trusted_session_absolute_timeout_plain ?? LOADING,
      detail: overview
        ? `Idle timeout ${overview.trusted_session_idle_timeout_plain}`
        : "Loading trusted-device policy.",
    },
  ];
}

/** The protections in force, from the server's startup configuration. */
export function signInProtectionFacts(overview: SecurityOverview): Fact[] {
  return [
    {
      label: "Sign-in security key",
      value: overview.session_signing_configured ? "On" : "Needs attention",
      toneClass: overview.session_signing_configured
        ? "mm-status-text--healthy"
        : "mm-status-text--failed",
    },
    {
      label: "HTTPS-only sign-in cookie",
      value: overview.sign_in_cookie_https_plain,
      toneClass:
        overview.sign_in_cookie_https_mode === "never"
          ? "mm-status-text--warning"
          : "mm-status-text--healthy",
    },
    {
      label: "Cross-site cookie protection",
      value: overview.sign_in_cookie_same_site,
    },
    {
      label: "Standard session",
      value: `Idle ${overview.standard_session_idle_timeout_plain}; max ${overview.standard_session_absolute_timeout_plain}`,
    },
    {
      label: "Trusted-device session",
      value: `Idle ${overview.trusted_session_idle_timeout_plain}; max ${overview.trusted_session_absolute_timeout_plain}`,
    },
    {
      label: "Extra HTTPS protection",
      value: overview.extra_https_hardening_enabled
        ? "On"
        : "Off — review HTTPS deployment",
      toneClass: overview.extra_https_hardening_enabled
        ? "mm-status-text--healthy"
        : "mm-status-text--warning",
    },
    {
      label: "Sign-in attempts allowed",
      value: `${overview.sign_in_attempt_limit} attempts / ${overview.sign_in_attempt_window_plain}`,
    },
    {
      label: "First-time setup attempts allowed",
      value: `${overview.first_time_setup_attempt_limit} attempts / ${overview.first_time_setup_attempt_window_plain}`,
    },
    {
      label: "Allowed web addresses",
      value: plural(
        overview.allowed_browser_origins_count,
        "configured origin",
        "configured origins",
      ),
    },
  ];
}

/** A row's own tone counts as needing attention when it is not the healthy one. */
export function needsAttention(fact: Fact): boolean {
  return (
    fact.toneClass === "mm-status-text--failed" ||
    fact.toneClass === "mm-status-text--warning"
  );
}
