import { describe, expect, it } from "vitest";

import type { FinishedKind } from "../activity/processing-outcome";
import {
  CONNECTION_LIGHT_MEANING,
  type ConnectionLight,
} from "../connections/connection-lights";
import {
  CONNECTION_STATE_MEANING,
  type ConnectionState,
} from "../connections/connection-model";
import { VERDICT_MEANING as DIRECT_PLAY_MEANING } from "../../components/processing/direct-play-line";
import {
  CLEAN_MEANING,
  FILE_MEANING,
} from "../../pages/activity/activity-entries";
import type {
  ProcessingDirectPlayVerdict,
  ProcessingFileStatus,
  ProcessingFileStoryStep,
} from "../processing/files-api";
import type { LibraryClean } from "../processing/library-cleans-api";
import { STORY_STEP_MEANING } from "../processing/story-step-meaning";
import type { Readiness } from "../processing/library-folder-chain-api";
import type { SystemLogLevel } from "../system/system-log-api";
import {
  LIBRARY_STATUSES,
  STATUS_MEANING as LIBRARY_STATUS_MEANING,
} from "../../pages/library/library-model";
import type { LibraryStatus } from "../../pages/library/library-model";
import type { LogLevel } from "../../pages/processing/dashboard/system/log-card-model";
import type { TaskState } from "../../pages/processing/dashboard/system/tasks-card-model";
import type { CheckTone } from "../../pages/processing/dashboard/system/health-checks";
import type { AreaTone } from "../../pages/processing/dashboard/system/health-card-model";
import type { FactTone } from "../../pages/processing/dashboard/system/this-weir-model";
import type { HandedBackTone } from "../../pages/processing/handed-back-model";
import type { StepState } from "../../pages/processing/stage-flow-model";
import type { CardTone } from "../../pages/processing/pipeline/pipeline-card-types";
import type { MmStatusTone } from "./mm-status-tone";
import { STATUS_MEANINGS } from "./status-meaning";
import type { StatusMeaning } from "./status-meaning";

/*
 * What the screens that show a state decide it means, beside the function the screen itself uses to decide: the two
 * must agree, so a screen cannot change what a state means without this table saying so. Each table is typed against
 * the model's own union, so a state added to a model does not compile until someone decides what it means here.
 */
const PRODUCTION_MEANINGS = {
  libraryStatus: {
    used: LIBRARY_STATUS_MEANING,
    expected: {
      needs_cleaning: "todo",
      cleaning: "doing",
      matches: "done",
      cant_clean_yet: "attention",
      left_alone: "idle",
    } satisfies Record<LibraryStatus, StatusMeaning>,
  },
  downloadStatus: {
    used: FILE_MEANING,
    expected: {
      unprocessed: "todo",
      out_of_schedule: "todo",
      processing: "doing",
      processed: "done",
      processing_failed: "broken",
      on_hold: "attention",
      blocked_upstream: "attention",
      passed_through: "attention",
      rejected: "attention",
      skipped: "idle",
      disabled: "idle",
      cancelled: "idle",
    } satisfies Record<ProcessingFileStatus, StatusMeaning>,
  },
  libraryClean: {
    used: CLEAN_MEANING,
    expected: {
      cleaned: "done",
      skipped: "done",
      failed: "broken",
    } satisfies Record<LibraryClean["outcome"], StatusMeaning>,
  },
  storyStep: {
    used: STORY_STEP_MEANING,
    expected: {
      neutral: "idle",
      good: "done",
      warn: "attention",
      bad: "broken",
    } satisfies Record<ProcessingFileStoryStep["tone"], StatusMeaning>,
  },
  directPlay: {
    used: DIRECT_PLAY_MEANING,
    expected: {
      yes: "done",
      maybe: "attention",
      no: "broken",
      unknown: "idle",
    } satisfies Record<ProcessingDirectPlayVerdict, StatusMeaning>,
  },
};

/*
 * Every state a model can produce, and the meaning it has. Each table is typed against its model's own union, so
 * a state added to a model does not compile until someone decides what it means here. When a model starts
 * producing a StatusMeaning itself, delete its row.
 */
const VOCABULARIES = {
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
  connectionLight: {
    asking: "doing",
    answered: "done",
    failed: "broken",
  } satisfies Record<ConnectionLight, StatusMeaning>,
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
  chipTone: {
    healthy: "done",
    info: "doing",
    warning: "attention",
    failed: "broken",
    neutral: "idle",
  } satisfies Record<MmStatusTone, StatusMeaning>,
};

/* The mappings the screens really use. Each must say what its table above says. */
const PRODUCTION_MAPS: Partial<
  Record<keyof typeof VOCABULARIES, Record<string, StatusMeaning>>
> = {
  connectionState: CONNECTION_STATE_MEANING,
  connectionLight: CONNECTION_LIGHT_MEANING,
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

  it.each(Object.entries(PRODUCTION_MAPS))(
    "%s: the screens give every state the meaning decided here",
    (name, used) => {
      expect(used).toEqual(VOCABULARIES[name as keyof typeof VOCABULARIES]);
    },
  );

  it.each(Object.entries(PRODUCTION_MEANINGS))(
    "%s: the screen gives every state the meaning decided for it",
    (_name, { used, expected }) => {
      expect(used).toEqual(expected);
      for (const meaning of Object.values(used)) {
        expect(STATUS_MEANINGS).toContain(meaning);
      }
    },
  );

  it("the Library table covers every status the Library lists", () => {
    expect(Object.keys(PRODUCTION_MEANINGS.libraryStatus.used).sort()).toEqual(
      [...LIBRARY_STATUSES].sort(),
    );
  });
});
