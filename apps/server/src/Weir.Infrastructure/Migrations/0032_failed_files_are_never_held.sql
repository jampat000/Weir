-- Weir schema 0032 (revision 0067_failed_files_are_never_held): one end state for a file Weir gave up on.
--
-- A file that failed three times in a row with the same class of failure was put on hold instead of being marked failed,
-- whatever the workflow's maximum attempts said. Such a file is now a failed file: same reason, no retry owed. A hold that
-- carries no failure attempts (waiting for a file to settle, for the workflow's hours) is untouched.
UPDATE files
SET status = 'processing_failed', hold_until = NULL, next_retry_at = NULL, updated_at = CURRENT_TIMESTAMP
WHERE status = 'on_hold' AND failure_attempts >= 3;
