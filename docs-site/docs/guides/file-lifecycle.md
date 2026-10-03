---
sidebar_position: 3
title: How Weir Keeps Your Files Safe
---

# How Weir keeps your files safe

Weir never touches your original file directly, and it never reports a file as done until the
cleaned copy has actually been checked. This page explains what that means in practice.

## Weir works on a copy, not your original

When Weir cleans a file, it writes the new copy — with the tracks you didn't ask for stripped out
— into a **work folder**, not straight into your output folder. Your original file in the watched
folder is only ever read, never modified, while this happens.

- If your workflow doesn't set its own work folder, Weir uses a default one under its own data
  folder, separate for movies and TV.
- Before it starts writing, Weir checks the drives it will write to still have the space your
  workflow keeps free: **Keep at least this many GB free on the drive this workflow writes to**
  (5 GB unless you change it, on each workflow's output settings). If they don't, the file is put
  **On hold** with the reason, and Weir tries again when there is room. It is never marked done
  and never started on a drive that can't finish it. The same check protects a file Weir hands
  back unchanged, and a file cleaned in place in your library.

## Nothing "half-written" ever appears in your output folder

Once the new copy is ready in the work folder, Weir doesn't just drop it into place. Getting the
finished file to your output folder is a two-step move:

1. **Publish under a hidden name first.** Weir copies (or, when it can, hard-links) the file into
   the output folder under a hidden temporary name — the same folder, but not the name you'll
   actually see.
2. **Swap it into place in one step.** Only once that hidden copy exists completely does Weir
   rename it to its real name. On the same drive, that rename is atomic: from the outside, the
   file either doesn't exist yet, or exists complete — there's no moment where a program watching
   that folder could see a partial file under its final name.

If anything goes wrong partway — the disk fills up, the process is interrupted, the source turns
out to have changed underneath it — Weir cleans up the hidden temporary file and leaves your
output folder exactly as it was. Nothing partial is ever left at the final path.

When the work folder and the output folder are on different drives (so a rename can't cross
between them), Weir copies into the hidden temporary file on the *destination* drive first, and
still only exposes it under its real name once that copy is complete.

## Weir hands your file back if it can't clean it

If Weir can't produce a usable result — the release turns out to have no tracks worth keeping, for
example — it doesn't leave a broken file behind or silently drop your download. It hands the
original file back unchanged and tells you why in Activity, so you (or your media manager) can
decide what to do next. That is the default. The workflow's **When retries run out** setting can
keep the file on hold or reject the release instead.

## When a file fails: tries, then one clear end

Each workflow has a **When a file fails** section. Weir tries a failed file again on its own, up to
**Maximum automatic attempts**. The first try counts, so 3 means the first try and two retries, and
nothing else limits it. The wait before each retry starts at **First retry delay** and doubles each
time, up to an hour, and Weir looks at the folder again shortly after the wait ends, not only at the
next scan. A file that fails the pre-check is only tried again if you switch on **Retry files that
failed the pre-check**.

When the tries run out, or the failure is not one the workflow retries, the file is **Failed** in
Activity, with the reason, and **When retries run out** decides the rest: hand the original back, keep it
until you deal with it, or reject the release. **Try again** on a failed file starts it again by hand,
whatever the limit.

A failed copy that was half written stays in the work folder only if you switch on **Keep a failed
file's half-written copy for a day** in Setup › Performance › Cleanup. **Leftover work files** removes it once it
is a day old. There is no job that deletes the downloads of failed files: remove one from Activity, where
Weir asks first.

## How long a file's history is kept

Everything Weir did to a file is in Activity. Weir keeps a file's history for as long as it still knows
the file, then for **Keep a file's history for N days after it's gone** (set in System › Logs › Log settings, under
**How long things are kept**, 90 by default, 0 for ever; Activity links to it). The days count from when Weir finds the file gone or forgotten, and a file
that comes back before then keeps its history. The same place keeps Weir's own log and
Activity, each with its own number of days.

## Changed your rules? Process rejected files again

A file your rules turn down is stored as **Rejected** and left alone, so a scan doesn't keep
checking it. After you change the rules, **Activity › Failed** offers **Process all again**. It asks
first, saying how many rejected files it will check again with your current rules. It covers every
rejected file in the workflow Activity is narrowed to, or in all workflows, not only the ones listed for
the period you are looking at. A file whose original is no longer in its watched folder is skipped and
counted. Nothing runs by itself when you change the rules.

## Subtitles and other extra files travel too, safely

If your rules keep sidecar files — subtitles, `.nfo` files, cover art — next to the video, Weir
copies them across to the output folder using the same safe-write process, renamed to match the
cleaned file. They're **copied, not moved**: if a subtitle can't be copied for some reason, Weir
leaves your original folder in place rather than deleting something it couldn't safely bring
across. If a file with the same name already exists in the output folder, Weir leaves it alone
rather than overwriting something you may have already edited.

## Deleting the original

Weir only deletes the original file (or the source folder it came from) after the cleaned copy
exists at its final path and any sidecars that needed to travel have been copied successfully. A
file that's missing is treated as already gone, not as a completed deletion with something hidden
behind it — and a file that's locked or in use produces a message you can read in Activity, not a
silent skip.

## Library mode: cleaning files you already have

Cleaning a file that's already in your library, in place, follows the same rule: Weir builds the
cleaned version alongside the original first and only swaps it in once the new copy is confirmed
good. It never leaves your library with a file half-replaced.

You set this up on the **Library** page, not in Setup: each library has its own folders, its own
rules profile (the workflow's until you choose another), the daily clean, and two checks made before
each clean. One skips a file when cleaning it would make the media manager download it again. The
other skips a file that is still seeding. **Files already in your library: keep the original after
cleaning** moves the original into an originals folder instead of deleting it, so removed tracks can
be recovered. Setup › Workflows is only about new downloads.
