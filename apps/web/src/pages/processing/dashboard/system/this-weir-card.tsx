import { CountUp } from "../../../../components/charts/count-up";
import { loadErrorMessage } from "../../../../lib/api/error-message";
import { useConfigurationBackupsQuery } from "../../../../lib/settings/queries";
import {
  useSystemOverviewQuery,
  useSystemStatsQuery,
} from "../../../../lib/system/use-system-stats";
import { classNames } from "../../../../lib/ui/class-names";
import { useNow } from "../../../../lib/ui/use-now";
import { BandCard, BandNote } from "./band-card";
import { FitText } from "./fit-words";
import { MoreCount } from "./more-count";
import {
  newestBackup,
  ringFigures,
  weirFacts,
  type Fact,
  type RingChecks,
} from "./this-weir-model";
import { useWholeRows } from "./use-whole-rows";

/** Once a second, so the uptime ticks. */
const TICK_MS = 1000;
/** A fact tile's height, and the gap between tiles, as the grid sets them (weir-system-band.css) in a card tall enough for three lines: whole rows of these fill the tiles' room. */
const TILE_PX = 42;
const TILE_GAP_PX = 5;
/** The ring's circle: r 32 in a 76 viewBox, so its length is 2πr. */
const RING_LENGTH = 2 * Math.PI * 32;

const wholeNumber = (value: number) => Math.round(value).toString();

function Ring({
  passing,
  fraction,
  needYou,
}: {
  passing: number;
  fraction: number;
  needYou: number;
}) {
  return (
    <div
      className={classNames("mm-sy-ring", needYou > 0 && "mm-sy-ring--need")}
    >
      <svg viewBox="0 0 76 76" aria-hidden="true">
        <circle className="mm-sy-ring__track" cx="38" cy="38" r="32" />
        <circle
          className="mm-sy-ring__value"
          cx="38"
          cy="38"
          r="32"
          strokeDasharray={RING_LENGTH}
          style={{ strokeDashoffset: RING_LENGTH * (1 - fraction) }}
        />
      </svg>
      <b>
        <CountUp value={passing} format={wholeNumber} />
      </b>
    </div>
  );
}

/**
 * One fact: its name, the reading, and a short line under it. A card too short for three lines makes a tile one line,
 * its short name and the reading, and keeps the line under it for the tooltip. Nothing in a tile is cut with an
 * ellipsis: its words are short, and a reading that has a shorter form (a long computer name) says that where it
 * would not fit.
 */
function FactTile({ fact }: { fact: Fact }) {
  const reading = fact.valueShort
    ? [fact.value, fact.valueShort]
    : [fact.value];
  return (
    <div
      className="mm-sy-fact"
      data-tone={fact.tone}
      title={`${fact.label}: ${fact.value}${fact.sub ? ` · ${fact.sub}` : ""}`}
    >
      <span className="mm-sy-fact__label">
        <span className="mm-sy-fact__long">{fact.label}</span>
        {fact.short === fact.label ? null : (
          <span className="mm-sy-fact__short">{fact.short}</span>
        )}
      </span>
      <FitText words={reading} className="mm-sy-fact__value" />
      <span className="mm-sy-fact__sub">{fact.sub}</span>
    </div>
  );
}

type ThisWeirCardProps = {
  /** The checks Health counts, so the ring and the Health card say the same. */
  checks: RingChecks;
  /** Takes the person to the Health card, from "N need you". */
  onShowHealth: () => void;
};

/**
 * Dashboard › System: how well Weir is doing, as a ring of the checks that pass with how many need you, and beside it
 * tiles for what Weir is running as and doing: its version, uptime, files at once, jobs, answer time, last backup,
 * address and size. The tiles' room decides how many whole rows show, and the header says how many more there are.
 */
export function ThisWeirCard({ checks, onShowHealth }: ThisWeirCardProps) {
  const overview = useSystemOverviewQuery();
  const stats = useSystemStatsQuery();
  const backups = useConfigurationBackupsQuery(true);
  const now = useNow(TICK_MS);
  const newest = newestBackup(backups.data?.items ?? []);
  const facts = overview.data
    ? weirFacts({
        overview: overview.data,
        work: stats.data
          ? { running: stats.data.now.running, slots: stats.data.now.slots }
          : null,
        lastBackupAt: newest?.created_at ?? null,
        lastBackupBytes: newest?.size_bytes ?? null,
        now,
      })
    : [];
  const { roomRef, gridRef, height, hidden } = useWholeRows(
    facts.length,
    TILE_PX,
    TILE_GAP_PX,
  );
  const ring = ringFigures(checks);
  return (
    <BandCard
      label="This Weir"
      aside={<MoreCount count={hidden} />}
      testId="system-weir"
    >
      {overview.data ? (
        <div className="mm-sy-weir">
          <div className="mm-sy-ringbox">
            <Ring
              passing={ring.passing}
              fraction={ring.fraction}
              needYou={ring.needYou}
            />
            <small className="mm-sy-ringbox__caption">
              of {ring.total.toLocaleString()}{" "}
              <span className="mm-sy-ringbox__long">checks </span>pass
            </small>
            {ring.needYou > 0 ? (
              <button
                type="button"
                className="mm-sy-needs"
                onClick={onShowHealth}
              >
                {ring.needYou.toLocaleString()} need you{" "}
                <span className="mm-sy-needs__arrow">→</span>
              </button>
            ) : (
              <span className="mm-sy-needs mm-sy-needs--good">All good</span>
            )}
          </div>
          <div ref={roomRef} className="mm-sy-facts-room">
            <div
              ref={gridRef}
              className="mm-sy-facts"
              style={height === undefined ? undefined : { height }}
              role="group"
              aria-label="Facts about Weir"
            >
              {facts.map((fact) => (
                <FactTile key={fact.key} fact={fact} />
              ))}
            </div>
          </div>
        </div>
      ) : (
        <BandNote>
          {overview.isError
            ? loadErrorMessage(overview.error, "how Weir is running")
            : "Reading how Weir is running…"}
        </BandNote>
      )}
    </BandCard>
  );
}
