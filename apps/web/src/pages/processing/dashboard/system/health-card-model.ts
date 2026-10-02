/** What the Health card says: the checks counted by area, the headline, and which checks the ribbon's filter shows. */
import {
  HEALTH_AREAS,
  type CheckTone,
  type HealthArea,
  type HealthCheck,
} from "./health-checks";

export type AreaTone = "bad" | "warn" | "ok";

export type AreaTally = {
  key: HealthArea;
  name: string;
  /** Checks that pass. */
  ok: number;
  /** Checks in the area, not counting notes. */
  total: number;
  tone: AreaTone;
};

export type HealthSummary = {
  pass: number;
  total: number;
  /** Checks that need you: a bad or a warning one. */
  need: number;
};

const needsYou = (tone: CheckTone): boolean =>
  tone === "bad" || tone === "warn";

/** Notes are facts, not checks: they never count towards a total. */
const isCounted = (check: HealthCheck): boolean => check.tone !== "note";

/** One tally for each area, in the ribbon's order. A bad check makes the area bad, a warning makes it a warning. */
export function areaTallies(checks: readonly HealthCheck[]): AreaTally[] {
  return HEALTH_AREAS.map(({ key, name }) => {
    const own = checks.filter((check) => check.area === key);
    const counted = own.filter(isCounted);
    return {
      key,
      name,
      ok: counted.filter((check) => check.tone === "ok").length,
      total: counted.length,
      tone: own.some((check) => check.tone === "bad")
        ? "bad"
        : own.some((check) => check.tone === "warn")
          ? "warn"
          : "ok",
    };
  });
}

export function healthSummary(checks: readonly HealthCheck[]): HealthSummary {
  const counted = checks.filter(isCounted);
  return {
    pass: counted.filter((check) => check.tone === "ok").length,
    total: counted.length,
    need: counted.filter((check) => needsYou(check.tone)).length,
  };
}

/**
 * The words beside the card's title: "9 of 10 pass · 1 needs you". While a folder check has not answered, Weir's
 * own count of its checks (`overview`) stands in, so the figure is there before every row is.
 */
export function healthHeadline(
  summary: HealthSummary,
  checking: boolean,
  overview: { passing: number; total: number } | null,
): string {
  const { pass, total } =
    checking && overview
      ? { pass: overview.passing, total: overview.total }
      : summary;
  if (total === 0) return "";
  const need =
    summary.need === 0
      ? "all good"
      : `${summary.need.toLocaleString()} ${summary.need === 1 ? "needs" : "need"} you`;
  return `${pass.toLocaleString()} of ${total.toLocaleString()} pass · ${need}`;
}

const TONE_ORDER: Record<CheckTone, number> = {
  bad: 0,
  warn: 1,
  idle: 2,
  ok: 3,
  note: 4,
};

/**
 * The checks the card lists: every check, or every check of the area picked, with the problems first and the worst of
 * them first, so what needs you is never below a row that is fine.
 */
export function listedChecks(
  checks: readonly HealthCheck[],
  area: HealthArea | null,
): HealthCheck[] {
  const wanted =
    area === null ? checks : checks.filter((check) => check.area === area);
  return [...wanted].sort((a, b) => TONE_ORDER[a.tone] - TONE_ORDER[b.tone]);
}

/** The newest time any check was looked at, or null when none has been. */
export function lastCheckedAt(checks: readonly HealthCheck[]): number | null {
  const times = checks.flatMap((check) =>
    check.checkedAt === null ? [] : [check.checkedAt],
  );
  return times.length > 0 ? Math.max(...times) : null;
}

/** What the list says when it has no row to show. */
export function emptyHealthWords(area: HealthArea | null): string {
  if (area === null) return "Weir is still looking.";
  const name = HEALTH_AREAS.find((entry) => entry.key === area)?.name ?? "";
  return `Nothing to show for ${name}.`;
}
