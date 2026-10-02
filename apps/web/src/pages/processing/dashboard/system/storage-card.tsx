import { Panel } from "../../../../components/panels/panel";
import { loadErrorMessage } from "../../../../lib/api/error-message";
import { useSystemStatsQuery } from "../../../../lib/system/use-system-stats";
import { classNames } from "../../../../lib/ui/class-names";
import { useFittingRows } from "../fit-rows";
import { MoreCount } from "./more-count";
import { driveBlocks, storageCount, type DriveBlock } from "./storage-model";
import { setupTabPath } from "../../../../lib/settings/setup-areas";

const STORAGE_PATH = setupTabPath("workflows");

function Drive({ block }: { block: DriveBlock }) {
  return (
    <li
      className={classNames("mm-sy-drive", block.low && "mm-sy-drive--low")}
      title={
        block.workflows ? `${block.path} · ${block.workflows}` : block.path
      }
      data-fit=""
      data-testid="system-drive"
    >
      <span className="mm-sy-drive__name">
        {block.name} <small>{block.path}</small>
      </span>
      <span className="mm-sy-drive__free">{block.freeLine}</span>
      <span className="mm-sy-drive__bar" aria-hidden="true">
        <i
          className="mm-sy-drive__weir"
          style={{ width: `${block.weirShare}%` }}
        />
        <i style={{ width: `${block.otherShare}%` }} />
        {block.keepFreeAt === null ? null : (
          <b
            className="mm-sy-drive__keep"
            style={{ left: `${block.keepFreeAt}%` }}
          />
        )}
      </span>
      {block.ioLine || block.keepFreeLine ? (
        <span className="mm-sy-drive__io">
          <span>{block.ioLine}</span>
          <span>{block.keepFreeLine}</span>
        </span>
      ) : null}
    </li>
  );
}

/**
 * Dashboard › System: one block for each drive any workflow reads from or writes to: how much room is left and when
 * it will be full, a bar of Weir's own work files and everything else with the line where Weir stops to keep room
 * free, and how busy the drive is. The card's height decides how many whole blocks show.
 */
export function StorageCard() {
  const stats = useSystemStatsQuery();
  const drives = stats.data?.drives ?? [];
  const blocks = driveBlocks(drives);
  const [listRef, fits] = useFittingRows();
  const more = blocks.length - Math.min(fits, blocks.length);
  return (
    <Panel
      title="Storage"
      count={storageCount(drives)}
      aside={<MoreCount count={more} />}
      to={STORAGE_PATH}
      toLabel="Storage"
      dataTestId="system-storage"
    >
      <div ref={listRef} className="mm-sy-drives-fit">
        {stats.isError && !stats.data ? (
          <p className="mm-sy-drives-note">
            {loadErrorMessage(stats.error, "the drives")}
          </p>
        ) : null}
        {stats.data && blocks.length === 0 ? (
          <p className="mm-sy-drives-note">
            No drive has been read yet, so free space is not known.
          </p>
        ) : null}
        <ul className="mm-sy-drives">
          {blocks.map((block) => (
            <Drive key={block.key} block={block} />
          ))}
        </ul>
      </div>
    </Panel>
  );
}
