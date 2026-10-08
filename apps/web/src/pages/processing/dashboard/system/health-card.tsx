import { useEffect, useMemo, useState } from "react";

import { Panel } from "../../../../components/panels/panel";
import type { ProcessingLibrary } from "../../../../lib/processing/libraries-api";
import { classNames } from "../../../../lib/ui/class-names";
import { motionAllowed } from "../../../../lib/ui/motion-allowed";
import { useNow } from "../../../../lib/ui/use-now";
import { CheckNowButton, useCheckNow } from "../check-now";
import { useFittingRows } from "../fit-rows";
import { HealthCheckRow } from "./health-check-row";
import {
  areaTallies,
  emptyHealthWords,
  healthHeadline,
  healthSummary,
  listedChecks,
} from "./health-card-model";
import type { HealthArea } from "./health-checks";
import { HealthRibbon } from "./health-ribbon";
import { MoreCount } from "./more-count";
import { useHealthChecks } from "./use-health-checks";
import { useSheen } from "./use-sheen";
import { WorkflowChainDrawer } from "./workflow-chain-detail";

/** The card's address on the page: the band's "N need you" scrolls here. */
export const HEALTH_CARD_ID = "system-health";
/** Seconds are shown, so "12s ago" moves once a second. */
const TICK_MS = 1000;
/** How long the card rings after "N need you" brought you to it. */
const FLASH_MS = 1300;

type HealthCardProps = {
  workflows: readonly ProcessingLibrary[];
  /** Goes up by one each time "N need you" is pressed: the card shows the problems, scrolls into view and rings. */
  jump?: number;
};

/**
 * Dashboard › System: everything Weir can look at, as checks. A ribbon of areas counts how many pass in each and
 * filters the list; with no area picked the list is the problems, each with "Fix it →" and "Check again". A workflow
 * opens its whole folder chain under "Details". The card's height decides how many whole rows show, and the header
 * says how many more there are.
 */
export function HealthCard({ workflows, jump = 0 }: HealthCardProps) {
  const health = useHealthChecks(workflows);
  const check = useCheckNow(health.health);
  const now = useNow(TICK_MS);
  const [picked, setPicked] = useState<HealthArea | null>(null);
  const [detailId, setDetailId] = useState<number | null>(null);
  const [flash, setFlash] = useState(false);
  const [listRef, fits] = useFittingRows();

  const tallies = useMemo(() => areaTallies(health.checks), [health.checks]);
  const summary = useMemo(() => healthSummary(health.checks), [health.checks]);
  const listed = useMemo(
    () => listedChecks(health.checks, picked),
    [health.checks, picked],
  );
  const sheen = useSheen(health.readAt);
  const more = listed.length - Math.min(fits, listed.length);
  const detail = health.health.workflows.find(
    (item) => item.workflow.id === detailId,
  );

  useEffect(() => {
    if (jump === 0) return undefined;
    setPicked(null);
    document.getElementById(HEALTH_CARD_ID)?.scrollIntoView?.({
      block: "nearest",
      behavior: motionAllowed() ? "smooth" : "auto",
    });
    setFlash(true);
    const stop = window.setTimeout(() => setFlash(false), FLASH_MS);
    return () => window.clearTimeout(stop);
  }, [jump]);

  return (
    <>
      <Panel
        id={HEALTH_CARD_ID}
        tabIndex={-1}
        title="Health"
        count={healthHeadline(summary, health.checking, health.overviewChecks)}
        aside={
          <>
            <MoreCount count={more} />
            <CheckNowButton check={check} />
          </>
        }
        dataTestId="system-health"
        className={classNames("mm-sy-card", flash && "mm-sy-card--flash")}
      >
        <HealthRibbon
          tallies={tallies}
          picked={picked}
          onPick={setPicked}
          sheen={sheen}
        />
        <div ref={listRef} className="mm-sy-fit">
          {listed.length === 0 ? (
            <p className="mm-sy-note">{emptyHealthWords(picked)}</p>
          ) : null}
          <ul className="mm-sy-list">
            {listed.map((item) => (
              <HealthCheckRow
                key={item.id}
                check={item}
                now={now}
                checking={health.busy.has(item.id)}
                onAgain={(target) => void health.again(target)}
                onDetails={setDetailId}
              />
            ))}
          </ul>
        </div>
      </Panel>
      {detail ? (
        <WorkflowChainDrawer
          item={detail}
          managers={health.health.managers}
          onClose={() => setDetailId(null)}
        />
      ) : null}
    </>
  );
}
