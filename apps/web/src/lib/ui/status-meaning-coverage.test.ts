import { describe, expect, it } from "vitest";

import type { FinishedKind } from "../activity/processing-outcome";
import type { ConnectionPhase } from "../connections/connection-activity";
import type { ConnectionState } from "../connections/connection-model";
import type { ProcessingDirectPlayVerdict } from "../processing/files-api";
import type { Readiness } from "../processing/library-folder-chain-api";
import type { SystemLogLevel } from "../system/system-log-api";
import { LIBRARY_STATUSES } from "../../pages/library/library-model";
import type { LibraryStatus } from "../../pages/library/library-model";
import type { LogLevel } from "../../pages/processing/dashboard/system/log-card-model";
import type { TaskState } from "../../pages/processing/dashboard/system/tasks-card-model";
import type { CheckTone } from "../../pages/processing/dashboard/system/health-checks";
import type { AreaTone } from "../../pages/processing/dashboard/system/health-card-model";
import type { GroupTone } from "../../pages/processing/dashboard/connections-card-model";
import type { FactTone } from "../../pages/processing/dashboard/system/this-weir-model";
import type { HandedBackTone } from "../../pages/processing/handed-back-model";
import type { StepState } from "../../pages/processing/stage-flow-model";
import type { CardTone } from "../../pages/processing/pipeline/pipeline-card-types";
import type { MmStatusTone } from "./mm-status-tone";
import { STATUS_MEANINGS } from "./status-meaning";
import type { StatusMeaning } from "./status-meaning";

/*
 * Every state a model can produce, and the meaning it has. Each table is typed against its model's own union, so
 * a state added to a model does not compile until someone decides what it means here. When a model starts
 * producing a StatusMeaning itself, delete its row.
 */
const VOCABULARIES = {
  libraryStatus: {
    needs_cleaning: "todo",
    cleaning: "doing",
    matches: "done",
    cant_clean_yet: "attention",
    left_alone: "idle",
  } satisfies Record<LibraryStatus, StatusMeaning>,
  finishedFile: {
    cleaned: "done",
    already: "done",
    passed: "attention",
    rejected: "attention",
    failed: "broken",
  } satisfies Record<FinishedKind, StatusMeaning>,
  connectionState: {
    ok: "done",
    slow: "attention",
    down: "broken",
    off: "idle",
    untested: "idle",
  } satisfies Record<ConnectionState, StatusMeaning>,
  connectionPhase: {
    asked: "doing",
    answered: "done",
    failed: "broken",
  } satisfies Record<ConnectionPhase, StatusMeaning>,
  connectionGroup: {
    ok: "done",
    slow: "attention",
    down: "broken",
    untested: "idle",
  } satisfies Record<GroupTone, StatusMeaning>,
  readiness: {
    ready: "done",
    not_verified: "attention",
    needs_attention: "attention",
  } satisfies Record<Readiness, StatusMeaning>,
  healthCheck: {
    ok: "done",
    warn: "attention",
    bad: "broken",
    idle: "idle",
    note: "idle",
  } satisfies Record<CheckTone, StatusMeaning>,
  healthArea: {
    ok: "done",
    warn: "attention",
    bad: "broken",
  } satisfies Record<AreaTone, StatusMeaning>,
  thisWeirFact: {
    good: "done",
    bad: "broken",
  } satisfies Record<FactTone, StatusMeaning>,
  scheduledTask: {
    running: "doing",
    ok: "done",
    failed: "broken",
    never: "idle",
  } satisfies Record<TaskState, StatusMeaning>,
  logLevel: {
    success: "done",
    info: "idle",
    warning: "attention",
    error: "broken",
  } satisfies Record<SystemLogLevel, StatusMeaning>,
  logCardLevel: {
    info: "idle",
    warning: "attention",
    error: "broken",
  } satisfies Record<LogLevel, StatusMeaning>,
  todayOutcome: {
    ok: "done",
    same: "done",
    warn: "attention",
  } satisfies Record<HandedBackTone, StatusMeaning>,
  stageStep: {
    done: "done",
    now: "doing",
    next: "todo",
    failed: "broken",
  } satisfies Record<StepState, StatusMeaning>,
  pipelineCard: {
    ok: "done",
    info: "doing",
    warn: "attention",
    bad: "broken",
    idle: "idle",
  } satisfies Record<CardTone, StatusMeaning>,
  directPlay: {
    yes: "done",
    maybe: "attention",
    no: "broken",
    unknown: "idle",
  } satisfies Record<ProcessingDirectPlayVerdict, StatusMeaning>,
  chipTone: {
    healthy: "done",
    info: "doing",
    warning: "attention",
    failed: "broken",
    neutral: "idle",
  } satisfies Record<MmStatusTone, StatusMeaning>,
};

describe("the meaning of every state a model can produce", () => {
  it.each(Object.entries(VOCABULARIES))(
    "%s: every state has a status meaning",
    (_name, states) => {
      for (const meaning of Object.values(states)) {
        expect(STATUS_MEANINGS).toContain(meaning);
      }
    },
  );

  it("the Library table covers every status the Library lists", () => {
    expect(Object.keys(VOCABULARIES.libraryStatus).sort()).toEqual(
      [...LIBRARY_STATUSES].sort(),
    );
  });
});
