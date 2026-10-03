import type { CurrentSession } from "../../../../lib/api/types";
import type { SecurityOverview } from "../../../../lib/settings/types";
import type { StatusMeaning } from "../../../../lib/ui/status-meaning";
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

/** One line of the Sign-in card: a name, what it says, and what to say when the value needs explaining. */
export type SignInRow = {
  label: string;
  value: string;
  /** Set when the value is a state, which then shows as a pill of this meaning. */
  meaning?: StatusMeaning;
  /** What the value means, as a hover note. */
  detail?: string;
};

function browserRow(
  session: CurrentSession | null | undefined,
  sessionFailed: boolean,
): SignInRow {
  if (!session) {
    return {
      label: "This browser",
      value: sessionFailed ? "Unavailable" : LOADING,
      meaning: sessionFailed ? "broken" : "doing",
      detail: sessionFailed
        ? "Could not read the current sign-in session."
        : "Checking the current sign-in session.",
    };
  }
  return session.trusted_device
    ? {
        label: "This browser",
        value: "Trusted",
        meaning: "done",
        detail: "Long-lived sign-in for this device",
      }
    : {
        label: "This browser",
        value: "Standard",
        meaning: "idle",
        detail: "Normal sign-in lifetime",
      };
}

/** How this browser is signed in, falling back to the server's policy until the session has loaded. */
export function currentSignInRows(
  session: CurrentSession | null | undefined,
  overview: SecurityOverview | undefined,
  sessionFailed: boolean,
): SignInRow[] {
  const idle = session
    ? formatSessionTimeout(session.idle_timeout_minutes)
    : overview?.standard_session_idle_timeout_plain;
  const longest = session
    ? plural(session.absolute_timeout_days, "day", "days")
    : overview?.standard_session_absolute_timeout_plain;
  return [
    browserRow(session, sessionFailed),
    {
      label: "Signs out after",
      value: idle && longest ? `${idle} idle · at most ${longest}` : LOADING,
    },
    {
      label: "Trusted devices",
      value: overview?.trusted_session_absolute_timeout_plain ?? LOADING,
      detail: overview
        ? `Idle timeout ${overview.trusted_session_idle_timeout_plain}`
        : undefined,
    },
  ];
}

/** One protection and whether it is in force. */
export type Protection = {
  label: string;
  value: string;
  /** True when the protection is off or weak and a person should look. */
  needsAttention: boolean;
};

/** The protections in force, from the server's startup configuration. */
export function signInProtections(overview: SecurityOverview): Protection[] {
  const httpsCookieOff = overview.sign_in_cookie_https_mode === "never";
  return [
    {
      label: "Sign-in security key",
      value: overview.session_signing_configured ? "On" : "Needs attention",
      needsAttention: !overview.session_signing_configured,
    },
    {
      label: "HTTPS-only sign-in cookie",
      value: overview.sign_in_cookie_https_plain,
      needsAttention: httpsCookieOff,
    },
    {
      label: "Cross-site cookie protection",
      value: overview.sign_in_cookie_same_site,
      needsAttention: false,
    },
    {
      label: "Standard session",
      value: `Idle ${overview.standard_session_idle_timeout_plain}; max ${overview.standard_session_absolute_timeout_plain}`,
      needsAttention: false,
    },
    {
      label: "Trusted-device session",
      value: `Idle ${overview.trusted_session_idle_timeout_plain}; max ${overview.trusted_session_absolute_timeout_plain}`,
      needsAttention: false,
    },
    {
      label: "Extra HTTPS protection",
      value: overview.extra_https_hardening_enabled
        ? "On"
        : "Off — review HTTPS deployment",
      needsAttention: !overview.extra_https_hardening_enabled,
    },
    {
      label: "Sign-in attempts allowed",
      value: `${overview.sign_in_attempt_limit} attempts / ${overview.sign_in_attempt_window_plain}`,
      needsAttention: false,
    },
    {
      label: "First-time setup attempts allowed",
      value: `${overview.first_time_setup_attempt_limit} attempts / ${overview.first_time_setup_attempt_window_plain}`,
      needsAttention: false,
    },
    {
      label: "Allowed web addresses",
      value: plural(
        overview.allowed_browser_origins_count,
        "configured origin",
        "configured origins",
      ),
      needsAttention: false,
    },
  ];
}
