-- Weir schema 0031 (revision 0066_workflow_free_space): the space to keep free on the output drive belongs to each workflow.
--
-- Settings › Performance carried one value for every workflow, though workflows can write to different drives. Each workflow
-- now has its own, and starts with the value Performance had, so nothing is checked against less than it was. The
-- Performance column stays in place, unread: a backup written before this migration still restores, and gives its own
-- workflows that value as they are restored.
ALTER TABLE libraries ADD COLUMN minimum_free_disk_space_mb INTEGER DEFAULT '5120' NOT NULL;
UPDATE libraries
SET minimum_free_disk_space_mb = (SELECT minimum_free_disk_space_mb FROM operator_settings WHERE id = 1)
WHERE EXISTS (SELECT 1 FROM operator_settings WHERE id = 1);
