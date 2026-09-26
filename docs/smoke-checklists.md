# Smoke Checklists

These checklists define the minimum user-level validation before calling a release ready. Automated CI checks are necessary but not enough; these are the click-through paths that catch packaging and first-run regressions.

## Windows installer smoke

Use the Velopack setup exe from the release being validated.

1. Run the setup exe on a clean Windows user profile or a reset test profile.
2. Confirm application files install under `%LocalAppData%\Weir`.
3. Confirm runtime data is created under `C:\ProgramData\Weir`.
4. Launch Weir from the Start Menu shortcut.
5. Confirm the tray icon appears and the browser opens the app on `http://localhost:9347/` (or the port shown in the tray's `Change port` item if 9347 was taken).
6. Confirm the **Create admin** screen appears when no user exists.
7. Attempt a password shorter than 8 characters and confirm it is blocked.
8. Create the first user with a valid password.
9. Confirm the setup wizard (**Set up Weir**) opens after the first user is created.
10. Confirm `Skip for now` leaves the wizard and lands on Processing.
11. Confirm the wizard can be reopened from System › About › Setup wizard › `Open setup wizard`.
12. Confirm `Finish setup` saves the time zone, the watched and output folders for the first Movies and TV libraries, and the automatic backup schedule.
13. Confirm the navigation shows Processing, History, Library, Settings and System, and that Processing is the first screen.
14. Confirm Settings shows the tabs Libraries, Rules, Media managers, Performance, Cleanup, Schedule and Alerts.
15. Confirm System shows the tabs About, Backups, Security and Logs.
16. In System › Backups, use **Export or restore now** to download a configuration backup.
17. Restore that backup and confirm the app remains usable.
18. Confirm System › About › Updates shows a meaningful status, even when no update is available.
19. Right-click the tray icon and confirm `Check for updates` is present and reaches the current release status.
20. In Settings › Libraries, open a library's editor and use `Browse` on a folder field to pick a local folder.
21. Enter a UNC path (`\\server\share\folder`) manually and confirm it saves; a missing folder is a warning, not a save blocker.
22. Open Library, pick the library from the title and confirm `Check again` runs.
23. Quit Weir from the tray icon.
24. Relaunch Weir and confirm the existing user, settings, and wizard completion state persist.
25. Install the next version over the current version and confirm Velopack applies a delta update cleanly.
26. Uninstall and reinstall only when intentionally testing clean-install behavior.
27. Confirm the automated packaged smoke reports that Processing placed a byte-identical
    pass-through file in the processed tree before removing its watched source.

## Docker smoke

Use the published release image, not a locally built image. The Git tag is `vX.Y.Z`; the image tag
drops the `v`, so `ghcr.io/jampat000/weir:3.2.7` is the image for tag `v3.2.7`. Run the checks on
both `linux/amd64` and `linux/arm64` when hardware for both is available.

1. Pull the versioned image:

   ```bash
   docker pull ghcr.io/jampat000/weir:X.Y.Z
   ```

2. Start with a fresh named volume:

   ```bash
   docker run --rm -p 9347:9347 -v weir-smoke:/data/weir ghcr.io/jampat000/weir:X.Y.Z
   ```

3. Open `http://localhost:9347/`.
4. Confirm the **Create admin** screen appears.
5. Confirm the release-candidate audit artifact includes `pass-through-proof.json`
   showing completed, byte-identical delivery and watched-source cleanup through a
   mounted Docker path.
6. Attempt a password shorter than 8 characters and confirm it is blocked.
7. Create the first user with a valid password.
8. Confirm the setup wizard opens.
9. Complete or skip the setup wizard and confirm System › About can reopen it.
10. Confirm `/health` returns healthy while the container is running.
11. Drop a file into a watched folder and confirm Processing shows it without a manual page reload, and that it then appears in History.
12. Confirm System › Logs shows application and runtime events, not developer build noise.
13. Confirm System › Backups can export and restore a configuration backup against the mounted volume.
14. Stop and restart the container with the same volume.
15. Confirm the user, settings, and runtime state persist.
16. Pull and run `latest` and confirm it resolves to the expected release digest.
17. Upgrade from the previous release tag to the new release tag using the same volume.
18. Confirm Docker path wording is clear: paths inside the container may differ from host/NAS paths.
19. Remove the smoke volume only after validation is complete.

## Failure handling

- Any failed smoke step gets a GitHub issue with the install type, version, exact step, expected result, actual result, and screenshots/logs with secrets removed.
- Release-blocking failures get `priority: critical`.
- Core workflow failures without data loss get `priority: high`.
