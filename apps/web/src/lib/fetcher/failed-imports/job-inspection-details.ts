/**
 * Operator-facing copy for job history "Details" — avoid raw log lines in the primary cell.
 */

function looksConnectionish(text: string): boolean {
  return /connection|connect|refused|unreachable|timeout|timed out|401|403|404|502|503|dns|certificate|ssl|tls|base url|host/i.test(
    text,
  );
}

export function jobInspectionDetailsForOperator(
  lastError: string | null | undefined,
  jobKind: string,
): { friendly: string; technical: string | null } {
  if (!lastError?.trim()) {
    return { friendly: "—", technical: null };
  }
  const raw = lastError.trim();
  const lower = raw.toLowerCase();
  const kindLower = jobKind.toLowerCase();

  if (kindLower.includes("sonarr") || /\bsonarr\b/i.test(raw)) {
    if (looksConnectionish(lower) || /missing|not configured|no url/i.test(lower)) {
      return {
        friendly: "Sonarr is missing connection details needed to run cleanup.",
        technical: raw,
      };
    }
  }
  if (kindLower.includes("radarr") || /\bradarr\b/i.test(raw)) {
    if (looksConnectionish(lower) || /missing|not configured|no url/i.test(lower)) {
      return {
        friendly: "Radarr is missing connection details needed to run cleanup.",
        technical: raw,
      };
    }
  }

  if (looksConnectionish(lower)) {
    return {
      friendly: "A network or connection problem stopped this run.",
      technical: raw,
    };
  }

  if (/sql|database|sqlite|integrity|locked/i.test(lower)) {
    return {
      friendly: "A database issue stopped this run.",
      technical: raw,
    };
  }

  return {
    friendly: "This run stopped with an error.",
    technical: raw,
  };
}
