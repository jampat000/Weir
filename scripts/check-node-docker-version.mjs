#!/usr/bin/env node
// The root .node-version file is the single source of truth for which Node major every setup-node
// step (node-version-file: .node-version) and the Dockerfile's web build stage run. This fails the
// build when the Dockerfile's node major drifts from .node-version, the way #788 briefly did by
// bumping only the Dockerfile.

import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPO = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const NODE_VERSION_FILE = path.join(REPO, ".node-version");
const DOCKERFILE = path.join(REPO, "Dockerfile");
const DOCKERFILE_NODE_IMAGE = /^FROM\s+node:(\d+)[^\s]*/m;

function nodeVersionFileMajor() {
  const raw = readFileSync(NODE_VERSION_FILE, "utf8").trim();
  const major = raw.match(/^v?(\d+)/)?.[1];
  if (!major) {
    console.error(`.node-version does not start with a version number: "${raw}"`);
    process.exit(1);
  }
  return major;
}

function dockerfileNodeMajor() {
  const text = readFileSync(DOCKERFILE, "utf8");
  const match = text.match(DOCKERFILE_NODE_IMAGE);
  if (!match) {
    console.error("Dockerfile has no `FROM node:<major>...` line to check.");
    process.exit(1);
  }
  return match[1];
}

const expected = nodeVersionFileMajor();
const actual = dockerfileNodeMajor();

if (expected !== actual) {
  console.error(
    `Dockerfile's node image is major ${actual}, but .node-version pins ${expected}. ` +
      "Update the Dockerfile's FROM line (and its digest) to match .node-version, or vice versa.",
  );
  process.exit(1);
}
console.log(`Dockerfile's node image (major ${actual}) matches .node-version.`);
