-- Weir schema 0041 (revision 0076_handoff_skipped_extras): a file the workflow's own rules left alone is not a failed file of its hand-off.
--
-- A folder hand-off recorded a file that a pass skipped under the workflow's minimum size as 'failed', so a hand-off that delivered its
-- main film and skipped an extra was reported failed, and Weir refused the manager's "imported" for it. A skipped file is now its own
-- result ('skipped').
--
-- 1. A target whose pass was skipped under the minimum size, recognised by the reason the pass recorded for it, is 'skipped'.
UPDATE media_manager_handoff_targets
SET result = 'skipped'
WHERE result = 'failed' AND message LIKE 'Skipped because this file is % MB, under the % MB minimum.%';

-- 2. A hand-off that was reported failed only because of such files is completed when another file was delivered and every skipped
--    file is smaller than each delivered one (the same rule the live status applies: a bigger skipped file might be the film). Sizes
--    are the sources' sizes in 'files'; a size that is unknown (0) leaves the hand-off as it was.
UPDATE media_manager_handoffs
SET reported_status = 'completed',
    state = CASE WHEN state = 'failed' THEN 'completed' ELSE state END
WHERE reported_status = 'failed'
  AND EXISTS (SELECT 1 FROM media_manager_handoff_targets t
              WHERE t.handoff_row_id = media_manager_handoffs.id AND t.result = 'skipped')
  AND EXISTS (SELECT 1 FROM media_manager_handoff_targets t
              WHERE t.handoff_row_id = media_manager_handoffs.id AND t.result IN ('completed', 'passed-through'))
  AND NOT EXISTS (SELECT 1 FROM media_manager_handoff_targets t
                  WHERE t.handoff_row_id = media_manager_handoffs.id AND (t.result IS NULL OR t.result = 'failed'))
  AND NOT EXISTS (
      SELECT 1
      FROM media_manager_handoff_targets skipped
      JOIN media_manager_handoff_targets delivered
        ON delivered.handoff_row_id = skipped.handoff_row_id AND delivered.result IN ('completed', 'passed-through')
      LEFT JOIN files skipped_file
        ON skipped_file.library_id = media_manager_handoffs.library_id AND skipped_file.relative_path = skipped.relative_path
      LEFT JOIN files delivered_file
        ON delivered_file.library_id = media_manager_handoffs.library_id AND delivered_file.relative_path = delivered.relative_path
      WHERE skipped.handoff_row_id = media_manager_handoffs.id AND skipped.result = 'skipped'
        AND (COALESCE(skipped_file.size_bytes, 0) = 0 OR COALESCE(delivered_file.size_bytes, 0) = 0
             OR skipped_file.size_bytes >= delivered_file.size_bytes));
