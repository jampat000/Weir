---
sidebar_position: 2
title: Windows Installer
---

# Windows Installer

Weir ships a Velopack-based Windows package. It installs as a desktop app with a .NET system tray host and supports automatic delta updates.

## Installation

1. Download the setup exe from [GitHub Releases](https://github.com/jampat000/Weir/releases)
2. Run the installer (no admin required)
3. Launch **Weir** from the Start Menu or desktop shortcut
4. The first time Weir starts, it asks which port to use (see [Choosing the port](#choosing-the-port))

## What gets installed

Relative paths below are inside the application folder.

| Component | Location |
|-----------|----------|
| Application folder | `%LocalAppData%\Weir\current` |
| Tray app (the main program) | `Weir.exe` |
| Weir server | `server\WeirServer.exe` |
| Web UI | `server\web-dist` |
| Bundled ffmpeg | `server\bin\ffmpeg` |
| Bundled mkvmerge (MKVToolNix) | `server\bin\mkvtoolnix` |
| Runtime data (SQLite, logs, backups) | `C:\ProgramData\Weir` |
| Saved port | `C:\ProgramData\Weir\port.txt` |

## How it runs

Weir runs in the user session, not as a Windows service. This avoids common NAS or external-drive access issues that affect Windows services.

The tray app (`Weir.exe`) starts the Weir server (`server\WeirServer.exe`, a self-contained .NET program) as a child process with `--port <port>`. It watches the server and restarts it if it stops unexpectedly. The server creates or updates its SQLite database itself when it starts.

The tray icon provides:
- **Open Weir** — opens the web UI in your browser (so does clicking the icon)
- **Open Data Folder** — opens the runtime data directory
- **Change port** — moves Weir to a different port and restarts it (the current port is shown in the menu)
- **Check for updates** — checks GitHub for a newer release; once one is found the item becomes **Download update**, then **Restart to update**
- **Quit** — stops Weir, and applies an update that has already downloaded

## Updates

The tray app installs updates itself, using Velopack:

- Delta updates keep downloads small
- No admin privileges required for updates
- **System › About** shows the running version, the latest release and its release notes, and has
  the **Update mode** setting: **Auto**, **Download only** or **Notify only**. Auto is the default.
- Weir starts again after an update without opening your browser, so an update never leaves a window behind on a computer nobody is watching. Starting Weir yourself still opens it.

## Choosing the port

The port is the number at the end of Weir's web address: with the default, Weir is at
`http://localhost:9347/` on the same computer, or `http://<computer-name>:9347/` from elsewhere on
your network. Weir's default is **9347** — it spells W-E-I-R on a phone keypad, and nothing else in a
typical media stack uses it.

| Service | Default port | Scope |
|---------|--------------|-------|
| Main server | 9347 | LAN (0.0.0.0) |

### On a desktop: Weir asks once

The installer (`Weir-win-Setup.exe`, built with Velopack) has no pages of its own to ask questions
on, so the tray asks instead, the first time it starts:

- **Use Weir's default port, 9347**, or **Use this port** and type your own.
- If another program on the computer already uses 9347, the window says so, and fills in the first
  free port above it.
- It will not accept a port that is not a whole number from 1 to 65535, or one another program is
  already using.
- **Quit** closes Weir without starting it; it asks again next time.

Your choice is saved in `C:\ProgramData\Weir\port.txt` and used on every start after that. Weir
never moves to a different port on its own: if the saved port is busy on a later start, it tells you
which port is busy and asks again. To move Weir later, use **Change port** in the tray menu; Weir
restarts on the new port and opens it in your browser. If the server cannot start on the new port,
Weir goes back to the old one and says so.

### Installing Weir from another program

A program driving Weir unattended — another installer, a provisioning script, a VM image builder —
must never run `Weir-win-Setup.exe` plain, even with `-- --port <number>` after it. Without
`--silent`, Setup shows its own install progress and then launches Weir, both of which assume a
person is at the interactive desktop; run with no one there to see or answer them, Setup can sit
running indefinitely with no window and no way to tell it is stuck (#779). Always pass `--silent`:

```
Weir-win-Setup.exe --silent
```

`--silent` hides every Setup dialog and prompt. Because Velopack's own post-install launch also
assumes an interactive session, `--silent` skips it too — so `-- <args>` after `--silent` reaches
nothing and should not be used. Setup then exits on its own within seconds: **0 on success,
non-zero on failure.** Nothing here waits on Weir itself, so there is no risk of Setup hanging on a
long-running app. `--installto <dir>` overrides the install directory if you need one other than the
per-user default (`%LocalAppData%\Weir`).

Weir is a foreground desktop app, not a Windows service (see "How it runs" above): once started it
keeps running, the same as it does for a person at a desktop. After a silent install, start it
explicitly and treat it as up once its own health endpoint answers — not once the process exits, it
is not supposed to:

```
"%LocalAppData%\Weir\current\Weir.exe" --port 9400 --silent
```

`--silent` on `Weir.exe` itself (a separate flag from Setup's own `--silent`) guarantees no UI at
all for this start — no first-run port dialog, no error message box, no browser tab — even if Weir
cannot tell whether the session is interactive. `--port` is honoured immediately, saved, and reused
on every later start; `WEIR_PORT` works the same way if your program would rather set an environment
variable than pass an argument:

```
setx WEIR_PORT 9400
```

`--port` wins over `WEIR_PORT`, and both win over the saved port. A supplied port is honoured even
if another program has it at that moment; the server then fails to start and says why in the log,
rather than Weir quietly picking another.

Poll `GET http://127.0.0.1:<port>/ready` until it answers `{"ready": true}`; a healthy start
typically answers within a few seconds, so a 60-second timeout is generous. If `Weir.exe` exits
before that, the start failed — its exit code is non-zero — and `tray-host.log` under the runtime
home (`C:\ProgramData\Weir` by default) says why.

If nothing is supplied and there is no desktop — a WinRM or SSH session, a scheduled task with no
logged-on user, a service — Weir never shows a window even without `--silent`. It uses 9347, or the
first free port above it if 9347 is taken, saves that, and records which port it chose and why in
`tray-host.log`. If a saved port is busy on a later start with no desktop, Weir does not move and
does not start; the log says which port is busy and how to choose another. Passing `--silent`
removes any dependence on that detection being right, so a program driving Weir unattended should
still pass it.

## Starting with Windows

Installing or updating Weir registers it to start when you sign in to Windows, as a `Weir` entry
under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. That start passes `--no-browser`, so
signing in doesn't open a browser window. If there is a `Weir.lnk` shortcut in your Startup folder,
Weir removes it so there is only one startup entry. Uninstalling removes both.

Uninstalling leaves the runtime data in `C:\ProgramData\Weir` in place.
