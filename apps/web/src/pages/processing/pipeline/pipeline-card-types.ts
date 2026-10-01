import type { ProcessingFile } from "../../../lib/processing/files-api";
import type { WorkSource } from "../processing-model";
import type { PipelineStage } from "./pipeline-stages";

export type CardTone = "info" | "warn" | "bad" | "ok" | "idle";

/** The one line under a card's title: what is happening to the file right now. */
export type CardStatus = {
  text: string;
  tone: CardTone;
  /** A dot pulses beside it: something is being worked on this moment. */
  pulse: boolean;
  /** The front of the text, drawn bold in the station's colour (a percentage, "✓ Delivered"). */
  lead?: string;
};

/**
 * The thin bar: how far along (`width`, in percent), or `waiting` when only "it is under way" is known and the
 * bar sweeps instead. `moving` shimmers it while work is being done.
 */
export type CardBar = { width: number; waiting: boolean; moving: boolean };

/** A piece of a detail line: plain words, or a bold one. */
export type DetailPart = string | { bold: string };

export type DetailLine = {
  parts: DetailPart[];
  /** The quiet text at the right of the line. */
  right?: string;
  /** The file's own name: small and monospaced. */
  mono?: boolean;
};

/** How a card that has just left the board ended: it was delivered, or stopped. */
export type CardEnd = "delivered" | "failed" | "rejected";

/** What a card says about a file, before it is placed on a station. */
export type CardWords = {
  status: CardStatus;
  bar: CardBar | null;
  /** What matters at this stage, most telling first; a card shows as many whole lines as its height holds. */
  details: DetailLine[];
};

export type PipelineCard = CardWords & {
  /** Stable for a file wherever it is on the board, so a card that changes station keeps its element and glides. */
  key: string;
  stage: PipelineStage;
  source: WorkSource;
  title: string;
  path: string;
  workflow: string;
  /** The file, to open its story. A library clean has none. */
  file: ProcessingFile | null;
  end: CardEnd | null;
};
