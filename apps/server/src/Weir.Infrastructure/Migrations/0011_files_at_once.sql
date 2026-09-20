-- Weir schema 0011 (revision 0046_files_at_once): "Files at once" means what it says (#633).
--
-- Three limits decided how many files ran together, and their defaults added up to one at a time however high
-- "Files at once" was set: each library had its own limit of 1, and the resolution budget (runner capacity with a
-- cost per resolution) capped whatever a folder scan queued.
--
-- 1. The resolution budget becomes a switch. Off, a file needs only a free slot.
ALTER TABLE operator_settings ADD COLUMN runner_budget_enabled BOOLEAN DEFAULT '0' NOT NULL;

--    It stays on for an install where it was doing something: one already running more than one file at once (the
--    budget was part of how many ran), or one whose costs keep a resolution from ever starting (a cost above the
--    capacity). Everywhere else it has never made a difference, so off changes nothing today.
UPDATE operator_settings
   SET runner_budget_enabled = 1
 WHERE max_concurrent_files > 1
    OR runner_cost_sd > runner_capacity
    OR runner_cost_720p > runner_capacity
    OR runner_cost_1080p > runner_capacity
    OR runner_cost_4k > runner_capacity
    OR runner_cost_undetermined > runner_capacity;

-- 2. A library's own limit of 0 now means "the same as Files at once", and is what a new library starts with. A
--    library still at the old default of 1 moves to it only where "Files at once" is itself 1, where the two are the
--    same thing today; raising "Files at once" later then does what it says. A library limit on an install that
--    already runs several at once is left exactly as saved: it may be deliberate.
UPDATE libraries
   SET max_concurrent_files = 0
 WHERE max_concurrent_files = 1
   AND (SELECT max_concurrent_files FROM operator_settings WHERE id = 1) = 1;
