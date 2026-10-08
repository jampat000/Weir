-- Weir schema 0040 (revision 0075_handoff_target_copy): when the copy a hand-off's file reported was written, so a manager's
-- word on the hand-off releases that copy and never a newer one Weir wrote for the same file afterwards. Null for a file
-- reported before this migration or without a recorded copy, which is matched by its path alone.
ALTER TABLE media_manager_handoff_targets ADD COLUMN output_written_at TEXT;
