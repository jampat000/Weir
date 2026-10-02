import { Chip } from "../../../../components/panels/chip";
import { ColumnsMenu } from "../../../../components/shared/columns-menu";
import { formatBytes } from "../../../../lib/format/bytes";
import type { ProcessingRulesPreviewResult } from "../../../../lib/processing/rules-preview-api";
import { useTableColumns } from "../../../../lib/ui/use-table-columns";
import { PREVIEW_COLUMNS } from "./rules-preview-columns";
import { RulesPreviewTable } from "./rules-preview-table";

/** What the rules would do to the previewed file: the verdict, every track, and the container notes. */
export function RulesPreviewResults({
  result,
}: {
  result: ProcessingRulesPreviewResult;
}) {
  const columns = useTableColumns(PREVIEW_COLUMNS);
  const reduction = result.estimated_size_reduction_bytes;
  // A file the plan would make bigger has no "smaller" to state.
  const sizeText =
    reduction != null && reduction >= 0 ? formatBytes(reduction) : "";
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2 text-sm">
        <Chip meaning={result.remux_required ? "todo" : "done"}>
          {result.remux_required
            ? "Weir would rewrite this file"
            : "Already matches these rules — no changes needed"}
        </Chip>
        {sizeText ? (
          <span className="text-mm-text3">
            Estimated size change: about {sizeText} smaller (estimate only)
          </span>
        ) : null}
        <div className="ml-auto">
          <ColumnsMenu table={columns} />
        </div>
      </div>

      <RulesPreviewTable tracks={result.tracks} columns={columns} />

      {result.original_language ? (
        <p className="mm-rules-preview__note">
          Original language: {result.original_language.note}
        </p>
      ) : null}

      {result.metadata_notes.length > 0 ? (
        <div>
          <h4 className="mm-rules-preview__subhead">Container changes</h4>
          <ul className="mm-rules-preview__list mt-1">
            {result.metadata_notes.map((note, index) => (
              <li key={index}>{note}</li>
            ))}
          </ul>
        </div>
      ) : null}

      {result.notes.length > 0 ? (
        <details className="mm-rules-preview__notes">
          <summary className="mm-rules-preview__subhead cursor-pointer">
            Full plan notes ({result.notes.length})
          </summary>
          <ul className="mm-rules-preview__list mt-2">
            {result.notes.map((note, index) => (
              <li key={index}>{note}</li>
            ))}
          </ul>
        </details>
      ) : null}
    </div>
  );
}
