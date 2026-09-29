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

The tray app (`Weir.exe`) starts the Weir server (`server\WeirServer.exe`, a self-contained .NET program) as a child process with `--port <port>`, listening for this PC only unless [LAN access](#firewall-and-lan-access) is on. It watches the server and restarts it if it stops unexpectedly. The server creates or updates its SQLite database itself when it starts.

The tray icon provides:
- **Open Weir** — opens the web UI in your browser (so does clicking the icon)
- **Open Data Folder** — opens the runtime data directory
- **Change port** — moves Weir to a different port and restarts it (the current port is shown in the menu)
- **Allow other devices on your network...** and **Only allow this PC** — see [Firewall and LAN access](#firewall-and-lan-access)
- **Check for updates** — checks GitHub for a newer release; once one is found the item becomes **Download update**, then **Restart to update**
- **Quit** — stops Weir, and applies an update that has already downloaded

Every stop the tray does itself (Quit, Change port, the LAN access items and Restart to update) asks the
server to shut down and waits up to 10 seconds, so running work and the database close in order. Only a
server that does not exit in that time is ended by force. `tray-host.log` in `C:\ProgramData\Weir` says which
happened: `stopped cleanly in 0.3 s`, or `did not stop in 10.0 s; killing it`.

## Updates

The tray app installs updates itself, using Velopack:

- Delta updates keep downloads small
- No admin privileges required for updates
- **System › About** shows the running version, the latest release and its release notes, and has
  the **Update mode** setting: **Auto**, **Download only** or **Notify only**. Auto is the default.
- Weir starts again after an update without opening your browser, so an update never leaves a window behind on a computer nobody is watching. Starting Weir yourself still opens it.

## Firewall and LAN access

Out of the box, Weir listens on this PC only (`127.0.0.1` and `[::1]`). Nothing else on your network can even
connect until you allow it. To let other devices on your network — a phone, another computer, Deluno on a
different machine — reach it at `http://<computer-name>:9347/`, turn **LAN access** on. Windows Firewall still has
the last word on whether they get through.

LAN access is one saved choice, kept in the data folder (`C:\ProgramData\Weir\lan-access`). It turns on when:

- you say yes to the question Weir asks once, the first time it starts after installing (not on a `--silent`
  install, which skips it — see below): **"Allow Weir on your network?"**, with a single Windows admin (UAC) prompt;
- you choose **Allow other devices on your network...** from the tray icon and approve the same admin prompt;
- a program runs `Weir.exe --allow-lan` (see [Unattended installs](#unattended-installs)).

Saying yes creates one inbound firewall rule named **Weir**, scoped to the installed server
(`server\WeirServer.exe`) and only the **Private** and **Domain** network profiles — never **Public**. It also
removes any block rule Windows itself created earlier for that program, for example one left behind if its own
"blocked some features" prompt was cancelled or never seen. Declining leaves nothing changed, and Weir does not
ask again automatically.

Changing LAN access restarts Weir, and the tray says so when it is done. **Only allow this PC** in the tray menu
turns LAN access off again: Weir restarts listening for this PC only. It leaves the firewall rule where it is,
because with nothing listening for the network the rule lets nothing in, and removing it would need another admin
prompt. Uninstalling Weir removes the rule when the uninstaller runs as administrator.

**Updating from an earlier version.** The first time a version with this setting starts, it keeps things as they
were: if Windows already allows Weir's server in (Weir's own **Weir** rule, or an allow rule Windows made when
someone clicked Allow on its own prompt), LAN access starts on, so devices that reach Weir today still do.
Otherwise it starts off. Weir saves that answer and does not work it out again.

**System › About** shows the current state plainly:

| What it says | What it means |
|---|---|
| Only this PC can reach Weir | LAN access is off. Use the tray item **Allow other devices on your network...** to change it. |
| Other devices on your network can reach Weir | LAN access is on and Windows Firewall lets the network you are on through. Use **Only allow this PC** to turn it off. |
| Windows Firewall is blocking other devices | LAN access is on, but a block rule exists, or the allow rule is missing, disabled, or does not cover the network you are on (Public is never covered). **Allow other devices on your network...** fixes the rule. |

This only appears on the Windows package: Docker and a bare source install manage their own network exposure and
say nothing here. Docker and Linux are unchanged: the server listens on every interface inside the container or
host, and you publish the port as usual.

### Unattended installs

A silent install (`Weir-win-Setup.exe --silent`, see below) never shows the admin prompt, and the Weir it starts
listens on this PC only. A program driving Weir unattended that also wants LAN access should pass `--allow-lan` to
the installed `Weir.exe`:

```
"%LocalAppData%\Weir\current\Weir.exe" --allow-lan
```

`--allow-lan` means "reachable on the LAN". It always turns LAN access on, with no prompt of any kind, and a Weir
that is already running restarts within a few seconds to listen for the network. When the process calling it is
already elevated it also creates the firewall rule. It never tries to elevate itself, because a caller driving Weir
unattended must never be handed a UAC prompt nobody is there to answer. If the process is not elevated, LAN access
is still turned on and the exit code is still 0, and `tray-host.log` says the rule was not created: until it
exists, Windows Firewall decides whether other devices get through, and **System › About** shows "Windows Firewall
is blocking other devices". `Weir.exe --configure-firewall` and `--remove-firewall` do only the rule (add/update,
and remove). They need an already-elevated process, they are what the tray menu and the first-run prompt run
themselves after their own UAC prompt, and they do not change LAN access.

## Choosing the port

The port is the number at the end of Weir's web address: with the default, Weir is at
`http://localhost:9347/` on the same computer, or `http://<computer-name>:9347/` from elsewhere on
your network. Weir's default is **9347** — it spells W-E-I-R on a phone keypad, and nothing else in a
typical media stack uses it.

| Service | Default port | Scope |
|---------|--------------|-------|
| Main server | 9347 | This PC only until LAN access is on, then every interface (0.0.0.0) |

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

A silent install never shows the [firewall admin prompt](#firewall-and-lan-access) either, and without
`--allow-lan` the Weir it starts is local-only: it listens on this PC (`127.0.0.1` and `[::1]`) and nothing else.
If other devices on the network need to reach this Weir, run the installed `Weir.exe --allow-lan` afterward (see
above). It turns LAN access on without prompting, and also creates the firewall rule when the caller is already
elevated; it never elevates itself.

Poll `GET http://127.0.0.1:<port>/ready` until it answers `{"ready": true}`; a healthy start
typically answers within a few seconds, so a 60-second timeout is generous. If `Weir.exe` exits
before that, the start failed — its exit code is non-zero — and `tray-host.log` under the runtime
home (`C:\ProgramData\Weir` by default) says why.

**Don't wait on the process tree.** Weir keeps running after Setup exits, by design — that is
success, not a hang — so wait for Setup's own exit, then poll `/ready` or `/health`; never treat
"every process this started has exited" or "its output stream closed" as the signal. That second
case matters if you capture a launched process's output: redirecting stdout/stderr through pipes (the
ordinary way — `Process.StandardOutput`/`StandardError` in .NET, `subprocess.communicate()` in
Python, and similar elsewhere) only reaches end-of-file once every process holding a duplicate of the
write end has closed it, including whatever that process goes on to start. `Weir.exe` closes any
stdio handles it inherited before doing anything else, so it — and `WeirServer.exe`, which it
starts — never keep a caller's pipes open (#779).

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
