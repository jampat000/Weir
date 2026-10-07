# Golden path

The run that gates every release tag of Deluno and Weir. This page is the same, word for word, in both repositories. A change to it is made in both, in the same words.

## Why

Green tests have shipped bugs that only showed when the product was used: a pause that did not pause, seeding originals deleted, the same file cleaned twice, a set-up that logged "could not set up", and a cleaned file shown as "handed back as it was". So before a tag is made, the exact commit is installed on a clean machine and used the way a person uses it. Decided by the owner, 7 Oct 2026 (Deluno #1158, Weir #903).

## When, where and who

- **When:** before the tag, on the exact commit that will be tagged. A release whose tagged commit has no passing record does not publish.
- **Where:** a clean Hyper-V Windows VM with a saved clean checkpoint, reverted before every run. The real-data machine is not the golden path.
- **What is installed:** Deluno's installer built from Deluno's commit, and Weir's Setup built from Weir's commit. Weir's comes from a manual CI run given the release it will become, `gh workflow run ci.yml --repo jampat000/Weir --ref main -f version=<version>`, as the artifact `weir-windows-<short sha>`. It reports `<version>+<short sha>`.
- **Who:** the Deluno session drives the run. The Weir session checks Weir's side, read-only and through Weir's own screens.
- **How each check is passed:** by clicking or watching, never by reading code or a test result. A check that fails stops the run. The fix gets a test that does what the user did, and the run starts again from step 1 on the new commit.

## The checklist, in run order

Copy this list into the run's record and tick each line with what was seen.

1. **Install.**
   - [ ] The clean checkpoint is reverted.
   - [ ] The exact-SHA Deluno installer runs. In the picker, tick the five download clients plus Weir. Weir's source is the exact-SHA `weir-windows-<sha>`, and the install log names its version and sha256.
   - [ ] The picker shows progress and a finish screen, and offers to open Deluno.
   - [ ] Weir answers on this PC only, and its tray starts silently.
2. **Clients come up ready.**
   - [ ] Each client has the Movies and TV categories and the documented folders (`C:\Downloads\Completed\<cat>`, `D:\Incomplete\<client>`).
   - [ ] Deluge's Label plugin is on.
   - [ ] Library Routing's folder check shows every folder as Matches.
3. **Account and guided setup, end to end.**
   - [ ] Every automatic choice is asked as Yes or No, and saved as answered.
   - [ ] **Connect Weir** works: it creates Weir's account, and Weir's Movies and TV workflows are set up from Deluno and linked, with the Folder chain all ✓ and nothing typed in Weir.
4. **Public trackers.**
   - [ ] At least three test Healthy, and the result is kept after Add.
   - [ ] Routing is automatic.
   - [ ] 1337x works through the Cloudflare helper.
5. **Usenet** (round two on).
   - [ ] The provider is entered once.
   - [ ] SABnzbd and NZBGet show Healthy only after it is set.
   - [ ] At least one Newznab indexer is Healthy.
6. **The real path.** One torrent film, one Usenet film, one TV episode, and one 4:3 film (Nosferatu, 1922, 1440×1080). For each:
   - [ ] Download, then hand-off to Weir with the completed path. Weir shows it moving through the lanes Incoming, Queued, Analysing, Processing and Delivering. Weir cleans it, and Deluno imports it, renamed.
   - [ ] Posters show.
   - [ ] The download client's folder still holds every original file: the video, the `.nfo` and every other sidecar.
   - [ ] Activity tells the story in plain words, in both apps. Weir's entry is correct: the tracks kept and removed, the size before and after, and what was saved.
7. **A title's own profile is honoured** in its search.
   - [ ] 4K and 720p are each targeted correctly, and each title's page shows its own profile as the target.
8. **Failure and recovery.**
   - [ ] A stalled torrent is replaced, and only it.
   - [ ] A failed hand-off sits under Needs action, is never deleted by the stall rules, and **Try again** from the download's drawer works.
   - [ ] Remove a title with its files, restore it from the recycle bin, and the title is back.
9. **Weir's pause and repeats.**
   - [ ] Pause Weir "until I resume" with **Keep looking for new files** on. A hand-off during the pause waits, and nothing is processed or removed. On resume it is processed once. Weir's Activity shows Paused and Resumed.
   - [ ] Send a hand-off again for a file already cleaned. It settles as "Skipped: already done", with no second output, and Deluno gets the same cleaned copy.
10. **Reaching Weir from another computer.**
    - [ ] In Weir's **System › About**, **Devices on my network** makes Weir reachable from another computer, with the VM's network set to Public.
11. **Screens.**
    - [ ] In Deluno, at 1920 and 1366 wide and at phone width: the poster Search and Refresh buttons work; Transfers, Activity, the Dashboard and System are clean, and every counted problem can be cleared on screen.
    - [ ] In Weir, Logs shows no unexpected warnings or errors, and every Activity entry has a readable title.
    - [ ] No issue numbers or old page names appear in either app's copy.
12. **Pass record.**
    - [ ] Set the `golden-path` commit status on both commits, with `target_url` pointing at the filled checklist:

      ```bash
      gh api repos/<owner>/<repo>/statuses/<sha> -f state=success -f context=golden-path -f description="Golden path passed" -f target_url=<checklist>
      ```

    - [ ] A failed run records `state=failure` on that commit instead, so an earlier success can never be used by mistake.

## After the run

The tag is made on the commit that passed, and each release's `golden-path` job (`scripts/verify-golden-path-for-release.mjs`) confirms the record before anything is published. Issues fixed in that release close with the run's evidence in the closing note.
