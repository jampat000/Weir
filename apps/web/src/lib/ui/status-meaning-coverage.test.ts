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
import {
  FOLDER_LINE_MEANING,
  READINESS_MEANING,
  type FolderChainLine,
  type Readiness,
} from "../processing/library-folder-chain-api";
import { updateMeaning } from "../settings/update-status";
import {
  LOG_LEVEL_MEANING,
  type SystemLogLevel,
} from "../system/system-log-api";
import type { SystemOverview } from "../system/system-stats-types";
import {
  LIBRARY_STATUSES,
  STATUS_MEANING as LIBRARY_STATUS_MEANING,
} from "../../pages/library/library-model";
import type { LibraryStatus } from "../../pages/library/library-model";
import {
  TASK_MEANING,
  type TaskState,
} from "../../pages/processing/dashboard/system/tasks-card-model";
import type { HandedBackTone } from "../../pages/processing/handed-back-model";
import type { StepState } from "../../pages/processing/stage-flow-model";
import type { CardTone } from "../../pages/processing/pipeline/pipeline-card-types";
import type { MmStatusTone } from "./mm-status-tone";
import { STATUS_MEANINGS } from "./status-meaning";
import type { StatusMeaning } from "./status-meaning";

/** The meaning of each status an update check can report, as the screens decide it. */
function updateStatuses(): Record<string, StatusMeaning> {
  const statuses = [
    "checking",
    "up_to_date",
    "update_available",
    "downloaded",
    "not_published",
    "unavailable",
  ];
  return Object.fromEntries(
    statuses.map((status) => [status, updateMeaning(status)]),
  );
}

/*
 * What the screens that show a state decide it means, beside the function the screen itself uses to decide: the two
 * must agree, so a screen cannot change what a state means without this table saying so. Each table is typed against
 * the model's own union, so a state added to a model does not compile until someone decides what it means here.
 */
const PRODUCTION_MEANINGS = {
  connectionState: {
    used: CONNECTION_STATE_MEANING,
    expected: {
      ok: "done",
      slow: "attention",
      down: "broken",
      off: "idle",
      untested: "idle",
    } satisfies Record<ConnectionState, StatusMeaning>,
  },
  connectionLight: {
    used: CONNECTION_LIGHT_MEANING,
    expected: {
      asking: "doing",
      answered: "done",
      failed: "broken",
    } satisfies Record<ConnectionLight, StatusMeaning>,
  },
  readiness: {
    used: READINESS_MEANING,
    expected: {
      ready: "done",
      not_verified: "attention",
      needs_attention: "attention",
    } satisfies Record<Readiness, StatusMeaning>,
  },
  folderChainLine: {
    used: FOLDER_LINE_MEANING,
    expected: {
      ok: "done",
      problem: "attention",
      note: "idle",
      unverified: "attention",
    } satisfies Record<FolderChainLine["state"], StatusMeaning>,
  },
  scheduledTask: {
    used: TASK_MEANING,
    expected: {
      running: "doing",
      ok: "done",
      failed: "broken",
      never: "idle",
    } satisfies Record<TaskState, StatusMeaning>,
  },
  logLevel: {
    used: LOG_LEVEL_MEANING,
    expected: {
      success: "done",
      info: "idle",
      warning: "attention",
      error: "broken",
    } satisfies Record<SystemLogLevel, StatusMeaning>,
  },
  updateStatus: {
    used: updateStatuses(),
    expected: {
      checking: "doing",
      up_to_date: "done",
      update_available: "todo",
      downloaded: "todo",
      not_published: "idle",
      unavailable: "attention",
    } satisfies Record<SystemOverview["update"]["status"], StatusMeaning>,
  },
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

describe("the meaning of every state a model can produce", () => {
  it.each(Object.entries(VOCABULARIES))(
    "%s: every state has a status meaning",
    (_name, states) => {
      for (const meaning of Object.values(states)) {
        expect(STATUS_MEANINGS).toContain(meaning);
      }
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
