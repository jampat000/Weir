import { useState } from "react";

import { Panel } from "../../../../components/panels/panel";
import { ColumnsMenu } from "../../../../components/shared/columns-menu";
import { useTableColumns } from "../../../../lib/ui/use-table-columns";
import type { NotificationChannelOut } from "../../../../lib/settings/types";
import { SaveModelNote } from "../../save-model-note";
import { alertColumns } from "./alert-columns";
import { ChannelTable } from "./channel-table";

/**
 * Every alert in its table, with the menu that puts the table's columns back. Its columns are made for the events it is
 * given, so whoever shows it gives it a `key` that changes with them.
 */
export function ChannelsPanel({
  channels,
  supportedEvents,
  editingId,
  deletingId,
  onEdit,
  onRemove,
}: {
  channels: NotificationChannelOut[];
  supportedEvents: string[];
  editingId: number | null;
  deletingId: number | null;
  onEdit: (id: number | null) => void;
  onRemove: (channel: NotificationChannelOut) => void;
}) {
  const [config] = useState(() => alertColumns(supportedEvents));
  const columns = useTableColumns(config);
  return (
    <Panel
      title="Where alerts go"
      count="Tick what each one hears about. Use Send test before relying on a new one."
      aside={
        <>
          <ColumnsMenu table={columns} />
          <SaveModelNote model="instant" />
        </>
      }
      padded
    >
      <ChannelTable
        channels={channels}
        supportedEvents={supportedEvents}
        columns={columns}
        editingId={editingId}
        deletingId={deletingId}
        onEdit={onEdit}
        onRemove={onRemove}
      />
      <p className="mm-quiet-note mt-4">
        A failure Weir will retry is sent only after its last try. When an alert
        does not answer, Weir notes it in System › Logs.
      </p>
    </Panel>
  );
}
