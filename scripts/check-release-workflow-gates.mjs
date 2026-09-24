import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

import { GATES } from "./ci-passed.mjs";
import { REQUIRED_EVIDENCE } from "./verify-ci-for-release.mjs";

// The release's safety rule: nothing reaches ghcr or the Releases page unless every check passed.
// release.yml runs its checks as parallel jobs, so the rule is expressed as the shape of the workflow:
// one `publish` job needs every other job and is the only one able to publish anything, and inside
// each job the steps come in a safe order. CI's side: ci-passed judges every ci.yml job, and the
// release's evidence (the ci-passed job and step) still exists under its name.

const workflowsDir = resolve(dirname(fileURLToPath(import.meta.url)), "..", ".github", "workflows");
const read = (name) => readFileSync(resolve(workflowsDir, name), "utf8").replace(/\r\n/g, "\n");
const release = read("release.yml");
const ci = read("ci.yml");
const ciContract = read("ci-contract.yml");
const ciPackaging = read("ci-packaging.yml");
const RELEASE = ".github/workflows/release.yml";
const CI = ".github/workflows/ci.yml";
const CI_CONTRACT = ".github/workflows/ci-contract.yml";
const CI_PACKAGING = ".github/workflows/ci-packaging.yml";

function requireText(source, marker, file) {
  const index = source.indexOf(marker);
  if (index < 0) {
    throw new Error(`${file} is missing required release gate marker: ${marker}`);
  }
  return index;
}

function requireOrder(source, markers, file) {
  let previous = -1;
  for (const marker of markers) {
    const current = requireText(source, marker, file);
    if (current <= previous) {
      throw new Error(`${file} has an unsafe release gate order near: ${marker}`);
    }
    previous = current;
  }
}

function jobIds(source) {
  const jobsStart = source.indexOf("\njobs:\n");
  if (jobsStart < 0) throw new Error("A workflow has no jobs: section.");
  return [...source.slice(jobsStart).matchAll(/\n {2}([a-zA-Z0-9_-]+):\n/g)].map((match) => match[1]);
}

function requireJob(source, jobName, file) {
  const marker = `\n  ${jobName}:\n`;
  const start = source.indexOf(marker);
  if (start < 0) {
    throw new Error(`${file} is missing required job: ${jobName}`);
  }
  const remaining = source.slice(start + marker.length);
  const nextJob = remaining.search(/\n  [a-zA-Z0-9_-]+:\n/);
  return nextJob < 0 ? remaining : remaining.slice(0, nextJob);
}

function rejectText(source, marker, file) {
  if (source.includes(marker)) {
    throw new Error(`${file} contains forbidden workflow text: ${marker}`);
  }
}

function needsOf(body, file) {
  const line = `\n${body}`.match(/\n {4}needs: \[([^\]]*)\]\n/);
  if (!line) throw new Error(`${file} must declare its needs as needs: [job, ...] on one line.`);
  return line[1].split(",").map((name) => name.trim()).filter(Boolean);
}

const invalidRunnerTempFixture = "WEIR_LIVE_E2E_FIXTURE_HOST_ROOT: ${{ runner.temp }}";
rejectText(release, invalidRunnerTempFixture, RELEASE);
rejectText(ciPackaging, invalidRunnerTempFixture, CI_PACKAGING);

// --- release.yml ------------------------------------------------------------------------------------

// No write access for the workflow as a whole; publish grants itself what it needs.
const releaseTop = release
  .slice(0, release.indexOf("\njobs:\n"))
  .split("\n")
  .filter((line) => !line.trimStart().startsWith("#"))
  .join("\n");
if (/:\s*write\b|write-all/.test(releaseTop)) {
  throw new Error(`${RELEASE} grants write access at workflow level; only the publish job may have it.`);
}

// Prereleases (v*-*) never run the release at all.
requireText(releaseTop, '    tags:\n      - "v*"\n      - "!v*-*"\n', `${RELEASE} on.push.tags`);

const releaseJobs = jobIds(release);
const publish = requireJob(release, "publish", RELEASE);

// Anything that can put something in public is confined to publish.
const publishingText = [
  "uses: docker/login-action@",
  "push: true",
  "docker push",
  "imagetools create",
  "packages: write",
  "contents: write",
  "uses: softprops/action-gh-release@",
  "gh release",
];
for (const job of releaseJobs.filter((name) => name !== "publish")) {
  const body = requireJob(release, job, RELEASE);
  for (const marker of publishingText) {
    rejectText(body, marker, `${RELEASE} job ${job} (only publish may publish)`);
  }
}

// publish waits for every other job, and only when they all succeeded.
const publishNeeds = needsOf(publish, `${RELEASE} publish`);
for (const job of releaseJobs.filter((name) => name !== "publish")) {
  if (!publishNeeds.includes(job)) {
    throw new Error(`${RELEASE} publish does not need ${job}; it could publish before ${job} passes.`);
  }
}
const publishIf = (publish.match(/\n {4}if: (.*)\n/) || [])[1] || "";
for (const override of ["always()", "failure()", "cancelled()", "success() ||"]) {
  if (publishIf.includes(override)) {
    throw new Error(`${RELEASE} publish's if: must not contain ${override}; it would run past a failed gate.`);
  }
}

// The version tag is pushed, checked and smoked, and the release published, before `latest` moves.
requireOrder(
  publish,
  [
    "uses: docker/setup-qemu-action@",
    "uses: docker/login-action@",
    "- name: Publish release Docker image",
    "platforms: linux/amd64,linux/arm64",
    "push: true",
    "- name: Verify published Docker manifest",
    "- name: Smoke test published Docker image",
    "- name: Prepare user-facing release notes",
    "- name: Publish GitHub Release",
    "- name: Tag the published image latest",
  ],
  `${RELEASE} publish job`,
);
const latestAt = publish.indexOf(":latest");
if (latestAt < 0 || latestAt < publish.indexOf("- name: Tag the published image latest")) {
  throw new Error(`${RELEASE} publish may name :latest only in its last step, after the GitHub Release exists.`);
}

// The tagged commit must be one ci.yml already passed on.
const ciPassed = requireJob(release, "ci-passed", RELEASE);
for (const marker of ["actions: read", "node scripts/verify-ci-for-release.mjs"]) {
  requireText(ciPassed, marker, `${RELEASE} ci-passed job`);
}

const validate = requireJob(release, "validate", RELEASE);
for (const marker of [
  "- name: Release notes file present",
  "node scripts/check-dotnet-vulnerabilities.mjs",
  "- name: Weir E2E smoke (Playwright + real .NET server)",
  "name: weir-web-dist",
]) {
  requireText(validate, marker, `${RELEASE} validate job`);
}

// The amd64 candidate: built unpushed, scanned, started, audited end to end, evidence kept.
const candidate = requireJob(release, "docker-candidate", RELEASE);
requireOrder(
  candidate,
  [
    "- name: Build unpushed Docker release candidate",
    "platforms: linux/amd64",
    "push: false",
    "- name: Trivy scan of Docker release candidate",
    "- name: Start unpushed Docker release candidate",
    "- name: Full live E2E against unpushed Docker release candidate",
    "- name: Upload Docker release-candidate evidence",
    "- name: Cleanup unpushed Docker release candidate",
  ],
  `${RELEASE} docker-candidate job`,
);
for (const marker of [
  "WEIR_LIVE_EXPECTED_VERSION: ${{ steps.version.outputs.plain }}",
  "WEIR_SESSION_COOKIE_SECURE=false",
  "WEIR_LIVE_E2E_FIXTURE_SERVER_ROOT: /e2e-fixture",
  "WEIR_LIVE_E2E_FIXTURE_HOST_ROOT:$WEIR_LIVE_E2E_FIXTURE_SERVER_ROOT",
  "name: weir-docker-release-candidate-audit",
  // The Trivy scan must actually be able to fail the release: a known HIGH/CRITICAL with a fix
  // available blocks it, and ignore-unfixed keeps that from being an unrelated, un-actionable CVE.
  "severity: HIGH,CRITICAL",
  "ignore-unfixed: true",
  'exit-code: "1"',
]) {
  requireText(candidate, marker, `${RELEASE} docker-candidate job`);
}

// Both published architectures must build before registry credentials exist.
requireOrder(
  requireJob(release, "docker-arm64", RELEASE),
  [
    "uses: docker/setup-qemu-action@",
    "- name: Build unpushed Docker release candidate (linux/arm64)",
    "platforms: linux/arm64",
    "push: false",
  ],
  `${RELEASE} docker-arm64 job`,
);

// Checksums describe the files as published. Signing rewrites Setup.exe, so hashing must come
// after it, and the provenance attestation must cover the same final bytes, so it comes after
// checksums and before upload.
const windowsSmoke = requireJob(release, "windows-smoke", RELEASE);
requireOrder(
  windowsSmoke,
  [
    "- name: Validate release version alignment",
    "- name: Sign Velopack release artifacts",
    "- name: Verify Velopack setup signature",
    "- name: Generate release artifact checksums",
    "- name: Attest provenance of Windows release artifacts",
    "- name: Upload Velopack release artifacts",
  ],
  `${RELEASE} windows-smoke job`,
);
for (const marker of ["id-token: write", "attestations: write", "uses: actions/attest-build-provenance@"]) {
  requireText(windowsSmoke, marker, `${RELEASE} windows-smoke job`);
}

// --- ci.yml and the workflows it calls --------------------------------------------------------------

const ciDockerSmoke = requireJob(ciPackaging, "docker-smoke", CI_PACKAGING);
requireOrder(
  ciDockerSmoke,
  [
    "- name: Build Weir Docker image",
    "- name: Start Weir Docker candidate",
    "- name: Full live E2E against Docker candidate",
    "- name: Upload Docker live-audit evidence",
    "- name: Cleanup Weir Docker smoke",
  ],
  `${CI_PACKAGING} docker-smoke job`,
);
for (const marker of [
  "WEIR_SESSION_COOKIE_SECURE=false",
  "WEIR_LIVE_E2E_FIXTURE_SERVER_ROOT: /e2e-fixture",
  "WEIR_LIVE_E2E_FIXTURE_HOST_ROOT:$WEIR_LIVE_E2E_FIXTURE_SERVER_ROOT",
  "name: weir-docker-live-audit",
]) {
  requireText(ciDockerSmoke, marker, `${CI_PACKAGING} docker-smoke job`);
}

// The contract suite runs one leg per area in areas.json, and every leg is part of CI's verdict.
requireText(requireJob(ci, "contract", CI), "areas: ${{ needs.changes.outputs.contract_areas }}", `${CI} contract job`);
const contractLeg = requireJob(ciContract, "area", CI_CONTRACT);
for (const marker of [
  "area: ${{ fromJSON(inputs.areas) }}",
  '--contract-area "$CONTRACT_AREA"',
  "WEIR_CONTRACT_LEDGER: ${{ runner.temp }}/",
]) {
  requireText(contractLeg, marker, `${CI_CONTRACT} area job`);
}

// ci-passed judges every other ci.yml job, always runs, and knows each job's flag.
const ciJobIds = jobIds(ci);
const verdictJob = requireJob(ci, "ci-passed", CI);
for (const marker of ["if: ${{ always() }}", "NEEDS: ${{ toJSON(needs) }}", "node scripts/ci-passed.mjs"]) {
  requireText(verdictJob, marker, `${CI} ci-passed job`);
}
const verdictNeeds = needsOf(verdictJob, `${CI} ci-passed`);
for (const job of ciJobIds.filter((name) => name !== "ci-passed")) {
  if (!verdictNeeds.includes(job)) {
    throw new Error(`${CI} ci-passed does not need ${job}, so CI could pass without it.`);
  }
}
const judged = ciJobIds.filter((name) => !["changes", "ci-passed"].includes(name)).sort();
if (JSON.stringify(judged) !== JSON.stringify(Object.keys(GATES).sort())) {
  throw new Error(
    `scripts/ci-passed.mjs GATES (${Object.keys(GATES).sort().join(", ")}) must list exactly the ci.yml jobs it judges (${judged.join(", ")}).`,
  );
}

// Every job and step the release gate asks CI for must still exist under that name.
function ciJobBody(displayName) {
  for (const id of ciJobIds) {
    const body = requireJob(ci, id, CI);
    const named = body.match(/^ {4}name: (.*)$/m);
    if ((named ? named[1] : id) === displayName) return body;
  }
  throw new Error(
    `${CI} has no job named "${displayName}", which scripts/verify-ci-for-release.mjs requires. Update one to match the other.`,
  );
}
for (const want of REQUIRED_EVIDENCE) {
  const body = ciJobBody(want.job);
  for (const step of want.steps) {
    requireText(body, `- name: ${step}\n`, `${CI} job "${want.job}"`);
  }
}

console.log(
  "Every release check gates publish, only publish can publish, `latest` moves last, the Docker candidate's " +
    "live E2E runs unpushed, and ci-passed judges every CI job and still carries the release's evidence.",
);
