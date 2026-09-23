*Historical record; not current documentation.*

# Plan: Live, Library and Settings (3.2 decisions)

| Date | Decision |
|------|----------|
| 2026-09-22 | A live view of every file in progress replaces Home as the first screen. It shipped as Processing. |
| 2026-09-22 | Settings uses one row of tabs across the top, not a second side menu. |
| 2026-09-22 | The Library screen's title is the library picker, rather than tabs that change with the number of libraries. |
| 2026-09-22 | History and logs is one list with a "Show" choice. It shipped as System › Logs. |
| 2026-09-22 | Library counts are chips beside search. Pause and the theme switch sit in each page's title row at the same height. |
| 2026-09-22 | Keeping the original after a library clean is available per library, off by default. |
| 2026-09-22 | Nothing scrolls sideways at any width. |
| 2026-09-23 | Display density is removed. Browser zoom covers size, and the Library list gets its own compact-rows switch. |
| 2026-09-23 | A file whose queued pass is cancelled reads **Cancelled**, a terminal state the scan leaves alone (#643). The live view shows nothing for it; it is history until someone queues it again. |
| 2026-09-23 | A library file gets the same per-track choice a held download already has (#501), and a per-file "leave this file alone" that a rescan honours. Both are kept in `library_file_marks`, beside the scan index, because a scan rewrites `library_files` from scratch every time. |
| 2026-09-23 | The server records when it last cleaned each library file, so "Cleaned" is a real count and a real filter. |
| 2026-09-23 | A track choice is checked twice: at the request, against the tracks the scan already read, so a choice that cannot apply is refused; and again in the job, against a fresh read plus the file's size when it was chosen, so a file that changed in between is left alone. |
