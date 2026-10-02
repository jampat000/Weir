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
  /** Shorter wordings of `text`, the fullest first, for a card too narrow to say it whole. */
  fits?: readonly string[];
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
  /** Briefer wordings of the whole line's left text, the fullest first, for a card too narrow to say it whole. */
  fits?: readonly string[];
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
  /** How many of the first detail lines the card never drops: it gives up a line of its title for them. */
  keep?: number;
  /** Everything the card knows, a sentence a fact, for its tooltip and accessible name. */
  fullFacts: string[];
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
