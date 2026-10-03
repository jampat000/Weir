import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { auditWithRetry, RegistryUnavailableError, unapprovedAdvisories } from "../../scripts/npm-audit-retry.mjs";

const siteRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const exceptions = JSON.parse(readFileSync(path.join(siteRoot, "dependency-audit-exceptions.json"), "utf8"));

let report;
try {
  // Retries only a registry-side failure (an outage or dropped connection); a real advisory in the
  // report is judged below exactly as before, on the first attempt.
  report = await auditWithRetry({ cwd: siteRoot });
} catch (error) {
  if (error instanceof RegistryUnavailableError) {
    console.error(`[docs-audit] ${error.message}`);
    console.error("[docs-audit] This is the npm registry, not a Weir problem. Re-run the job once npm's audit endpoint recovers.");
    process.exit(1);
  }
  throw error;
}

// Every advisory, whatever its severity, needs a current exception with a mitigation.
const unapproved = unapprovedAdvisories(report, { exceptions: exceptions.exceptions, level: "info" });
if (unapproved.length) {
  console.error("[docs-audit] Dependency audit has an unapproved or expired finding:");
  for (const item of unapproved) console.error(`- ${item}`);
  process.exit(1);
}

console.log(`[docs-audit] ${exceptions.exceptions.length} exception(s) cover every advisory in the report.`);
