import type { SegmentedOption } from "../../components/panels/segmented-control";
import type { WorkSource } from "./processing-model";

/** Which kind of work the page shows: everything, new downloads, or files already in a library. */
export type Filter = "all" | WorkSource;

export const FILTER_OPTIONS: readonly SegmentedOption<Filter>[] = [
  { value: "all", label: "Everything" },
  { value: "download", label: "New downloads" },
  { value: "library", label: "Library cleaning" },
];

/** Today's figures are counted over the last day. */
export const TODAY_DAYS = 1;
