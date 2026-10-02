import { LiveTrace } from "../../../../components/charts/live-trace";
import { loadErrorMessage } from "../../../../lib/api/error-message";
import { useSystemStatsQuery } from "../../../../lib/system/use-system-stats";
import { classNames } from "../../../../lib/ui/class-names";
import { BandCard, BandNote, TenMinutes } from "./band-card";
import { TraceColumn } from "./trace-column";
import { computerColumns, machineTags } from "./this-computer-model";

/** What the card's traces span, in words and in ms. */
const SPAN_WORDS = "last 10 minutes";

/**
 * Dashboard › System: the machine's CPU, memory and disk over the last ten minutes. Each is a glowing swatch, a
 * figure that counts to its new value, a line saying how much of it is Weir's own, and a trace that slides along
 * as each second's reading arrives.
 */
export function ThisComputerCard() {
  const stats = useSystemStatsQuery();
  const data = stats.data;
  return (
    <BandCard
      label="This computer"
      aside={<TenMinutes />}
      className="mm-sy-band--computer"
      testId="system-computer"
    >
      {data ? (
        <>
          <div className="mm-sy-cols mm-sy-cols--three">
            {computerColumns(data).map((column) => (
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
                  series={[
                    {
                      key: column.key,
                      colour: column.colour,
                      samples: column.samples,
                    },
                  ]}
                  windowMs={data.window_s * 1000}
                  top={column.scale.kind === "percent" ? 100 : null}
                  floorTop={
                    column.scale.kind === "rate" ? column.scale.floorTop : 0
                  }
                  format={column.readout}
                  label={`${column.label}, ${SPAN_WORDS}`}
                />
              </TraceColumn>
            ))}
          </div>
          <div className="mm-sy-tags">
            {machineTags(data.machine).map((tag) => (
              <span
                key={tag.text}
                className={classNames(
                  "mm-sy-tag",
                  tag.meaning && "mm-status-pill",
                )}
                data-status={tag.meaning}
              >
                {tag.text}
              </span>
            ))}
          </div>
        </>
      ) : (
        <BandNote>
          {stats.isError
            ? loadErrorMessage(stats.error, "this computer's readings")
            : "Reading this computer…"}
        </BandNote>
      )}
    </BandCard>
  );
}
