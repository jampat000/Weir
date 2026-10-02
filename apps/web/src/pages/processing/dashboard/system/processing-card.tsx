import { LiveTrace } from "../../../../components/charts/live-trace";
import { loadErrorMessage } from "../../../../lib/api/error-message";
import { useSystemStatsQuery } from "../../../../lib/system/use-system-stats";
import { useNow } from "../../../../lib/ui/use-now";
import { BandCard, BandNote } from "./band-card";
import { TraceColumn } from "./trace-column";
import { processingColumns, workPills } from "./processing-card-model";
import { useRecentWork } from "./use-recent-work";

const SPAN_WORDS = "last 10 minutes";
/** The finished-work window moves a minute at a time, so once a second is as often as it needs to be measured. */
const TICK_MS = 1000;

/**
 * Dashboard › System: Weir's own work over the last ten minutes: what the running passes read and write, and how
 * fast they go, with how many are running and what the last ten minutes finished and saved.
 */
export function ProcessingCard() {
  const stats = useSystemStatsQuery();
  const now = useNow(TICK_MS);
  const recent = useRecentWork(now);
  const data = stats.data;
  return (
    <BandCard
      label="Processing"
      aside={SPAN_WORDS}
      className="mm-sy-band--processing"
      testId="system-processing"
    >
      {data ? (
        <>
          <div className="mm-sy-cols mm-sy-cols--two">
            {processingColumns(data).map((column) => (
              <TraceColumn
                key={column.key}
                colour={column.colour}
                label={column.label}
                value={column.value}
                figure={column.figure}
                unit={column.unit}
                sub={column.sub}
              >
                <LiveTrace
                  series={column.lines.map((line) => ({
                    key: line.key,
                    label: line.label,
                    colour: line.colour,
                    samples: line.samples,
                  }))}
                  windowMs={data.window_s * 1000}
                  top={null}
                  floorTop={column.floorTop}
                  format={column.readout}
                  label={`${column.label}, ${SPAN_WORDS}`}
                />
              </TraceColumn>
            ))}
          </div>
          <div className="mm-sy-tags">
            {workPills(data.now, recent).map((pill) => (
              <span key={pill.key} className="mm-sy-tag">
                {pill.text}
              </span>
            ))}
          </div>
        </>
      ) : (
        <BandNote>
          {stats.isError
            ? loadErrorMessage(stats.error, "Weir's work")
            : "Reading Weir's work…"}
        </BandNote>
      )}
    </BandCard>
  );
}
