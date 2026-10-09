-- Weir schema 0042 (revision 0077_file_skip_kind): why a file was skipped, as a code, so nothing has to read the sentence in status_reason.
--
-- Meaningful only while the file's status is 'skipped'. Null for a skip without a code, which is every skip but one under the workflow's
-- minimum size.
ALTER TABLE files ADD COLUMN skip_kind TEXT;

-- Rows skipped before the code existed carry only the sentence the skip recorded (LibraryAdmission.BelowMinimumSizeReason). This is the one
-- place that reads it.
UPDATE files
SET skip_kind = 'below_minimum_size'
WHERE status = 'skipped' AND status_reason LIKE 'Skipped because this file is % MB, under the % MB minimum.%';
