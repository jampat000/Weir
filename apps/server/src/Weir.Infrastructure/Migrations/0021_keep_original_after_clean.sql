-- Weir schema 0021 (revision 0056_keep_original_after_clean): #735, "keep the original after a library clean".
--
-- Off by default (every existing library keeps today's behaviour: the pre-clean original is deleted once the
-- cleaned copy is safely in place). On: the swap moves the original into originals_folder instead of deleting it
-- (blank means the default ".weir-originals" folder inside whichever library folder held the file).
ALTER TABLE libraries ADD COLUMN keep_original_after_clean BOOLEAN DEFAULT '0' NOT NULL;
ALTER TABLE libraries ADD COLUMN originals_folder TEXT DEFAULT '' NOT NULL;

-- Where a swap in progress will (or did) put a kept original, so a crash between deciding that path and finishing
-- the move is recovered from the journal row rather than guessed at again. Null when the setting is off.
ALTER TABLE library_swaps ADD COLUMN kept_original_path TEXT;
