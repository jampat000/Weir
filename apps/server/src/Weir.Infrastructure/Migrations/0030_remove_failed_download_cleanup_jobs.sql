-- Weir schema 0030 (revision 0065_remove_failed_download_cleanup_jobs): the "Downloads of failed files" cleanup is gone, and
-- nothing runs its job kinds any more, so work still queued for it is dropped rather than left to wait for ever. Finished
-- rows stay until job retention removes them.
DELETE FROM jobs
WHERE job_kind IN ('processing.movie_failure_cleanup_sweep.v1', 'processing.tv_failure_cleanup_sweep.v1')
  AND status IN ('pending', 'leased');
