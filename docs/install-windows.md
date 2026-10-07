# Install Weir on Windows

This takes about five minutes. You need Windows 10 or 11 (64-bit) and a normal user account. You do not
need administrator rights to install.

Weir is in its release-candidate stage, so the installer is on the [Releases page](https://github.com/jampat000/Weir/releases)
marked **Pre-release**. It is not the "Latest" release, so use the list, not the Latest button.

## 1. Download the installer

1. Open the [Releases page](https://github.com/jampat000/Weir/releases).
2. Pick the newest release at the top.
3. Under **Assets**, download **`Weir-win-Setup.exe`**.

## 2. Run it

Double-click `Weir-win-Setup.exe`.

Weir is not code-signed, so Windows may stop you. This is expected, and you can read the source and the
build steps in this repository if you want to check what you are running.

- **"Windows protected your PC"** (SmartScreen): choose **More info**, then **Run anyway**.
- **Your browser may warn that the file "isn't commonly downloaded".** Choose **Keep** (Edge) or
  **Keep anyway** (Chrome).

The installer needs no choices. It puts Weir in `%LocalAppData%\Weir`, adds a Start Menu and desktop
shortcut, and starts Weir.

## 3. Answer two questions

The first time Weir starts, it asks two things, one after the other.

**"Allow Weir on your network?"** This is the Windows Firewall question. It decides who can reach Weir.

| You choose | What happens |
| --- | --- |
| **No** | Weir can only be reached from this PC. Nothing else on your network can even connect. This is the safe default and the right answer if Weir and your browser are on the same PC. |
| **Yes** | Windows asks for administrator approval once, then Weir is reachable from other devices on your home network (a phone, another computer, Deluno or Radarr on another machine). Weir adds one firewall rule named **Weir**, for every network profile (**Domain**, **Private** and **Public**), because Windows often marks a home network as Public. Weir still needs its own sign-in. |

You can change your mind later, in either of two places. In Weir, open **System › About** and choose
**Devices on my network** or **This PC only**. Or right-click the Weir icon in the system tray and choose
**Allow other devices on your network...** or **Only allow this PC**. Weir restarts when you do.

**"Start Weir" and a port number.** Keep the default, **9347**, unless something else on the PC already uses
it. In that case the window says so and suggests the next free port. Weir remembers your choice.

## 4. Create your account

Your browser opens Weir at `http://localhost:9347/`. It asks for a username and password. This creates the
admin account, and there is no separate sign-up.

Because you are on the same PC, Weir does not ask for a setup code. If you create the account from another
device instead, Weir asks for a code. It is in the file `C:\ProgramData\Weir\setup-code` on the PC running
Weir.

Next, Weir's setup wizard asks how your downloads reach it and which folders to watch. That part is the same
on every install. The [Quickstart](../docs-site/docs/quickstart.md#3-follow-the-setup-wizard) walks through it.

## Where things are

| What | Where |
| --- | --- |
| The program | `%LocalAppData%\Weir` (shortcut: Start Menu, **Weir**) |
| Your data: database, logs, backups, settings | `C:\ProgramData\Weir` |
| The port you chose | `C:\ProgramData\Weir\port.txt` |
| The tray log (why Weir did something at start-up) | `C:\ProgramData\Weir\tray-host.log` |

The data folder is locked to your Windows account, so other accounts on the PC cannot read it.

Weir runs as you, in your own sign-in session. It is not a Windows service. That is on purpose: a service
cannot see mapped network drives, and a NAS is where most media lives. It starts with Windows when you sign
in and does not open a browser window when it does.

## The tray icon

Weir lives in the system tray, next to the clock. If you do not see it, click the small arrow to show
hidden icons. Click the icon to open Weir in your browser. Right-click it for the menu:

- **Open Weir**
- **Open Data Folder**
- **Change port**
- **Allow other devices on your network...** (or **Only allow this PC**)
- **Check for updates**
- **Quit**

Quitting asks Weir to finish properly first, so running work and the database close in order.

## Open Weir from another computer

Only after you have allowed other devices on your network (step 3). On the other computer, open
`http://<your-pc-name>:9347/`. **System › About** shows the exact address and has a **Copy** button.

Weir is meant for your home network. Do not forward port 9347 on your router. To reach Weir from outside
your home, put it behind a reverse proxy with HTTPS. See the [reverse proxy guide](../docs-site/docs/deployment/reverse-proxy.md).

## Update Weir

Weir checks GitHub for new versions and tells you through the tray icon. Under **System › About**, **Update
mode** decides what happens:

- **Auto**: downloads and installs by itself (the default).
- **Download only**: downloads, then waits for you to choose **Restart to update**.
- **Notify only**: tells you, nothing more.

Updates are small, need no administrator rights, and keep your data. After one, Weir starts again quietly,
without opening a browser window.

While Weir is a release candidate, the in-app check only looks at stable releases, not pre-releases. To move
from one release candidate to the next, download the new `Weir-win-Setup.exe` from the Releases page and run
it. It installs over the old one. Your data is not touched.

Before a big upgrade, take a backup under **System › Backups**.

## Uninstall

1. Open **Settings › Apps › Installed apps**.
2. Find **Weir**, open its menu and choose **Uninstall**.

| Removed | Kept |
| --- | --- |
| The program in `%LocalAppData%\Weir` | Your data in `C:\ProgramData\Weir` (database, logs, backups, settings) |
| Start Menu and desktop shortcuts | The files in your own media folders. Weir never deletes those by uninstalling |
| The "start with Windows" entry | |
| The **Weir** firewall rule, only if you ran the uninstaller as administrator. Otherwise it stays and does nothing, because the program it names is gone | |

Weir is stopped for you before it is removed.

To remove everything, delete `C:\ProgramData\Weir` afterwards. To move to a new PC, copy that folder
before you delete it. Reinstalling on the same PC finds your data again.

## Installing from a script

Setting Weir up on many machines, or from another installer? Plain `Weir-win-Setup.exe` assumes a person is at
the screen. Use `Weir-win-Setup.exe --silent`. The details, including how to choose a port and allow your
network without a prompt, are in the [Windows installer reference](../docs-site/docs/deployment/windows.md#installing-weir-from-another-program).

## If something goes wrong

- **The browser does not open.** Click the Weir tray icon. If there is no icon, start **Weir** from the Start Menu.
- **"Port in use".** Pick another port in the first-run window, or later with **Change port**.
- **Other devices cannot connect.** Open **System › About**. Under **This PC** it says whether network access is
  on, and shows "Blocked by Windows Firewall" if Windows is stopping other devices. **Try again** asks Windows once more.
- **Anything else.** Read `C:\ProgramData\Weir\tray-host.log` and **System › Logs**, then
  [open an issue](https://github.com/jampat000/Weir/issues) and include what they say.
