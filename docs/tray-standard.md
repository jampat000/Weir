# The tray standard (Deluno and Weir)

Both apps keep an icon in the Windows notification area. This page is the one standard for what that icon shows, what its menu holds and how it behaves. Deluno and Weir each keep a word-for-word copy, and each app builds and tests its own tray. Nothing is shared in code, so neither app depends on the other.

Decided by the owner on 9 Oct 2026, from the side-by-side audit of both trays.

## What the icon shows

Each app keeps its own brand icon, so you always know which is which. A small corner mark appears only when something is different. With no mark, all is well.

| Mark | Meaning |
|---|---|
| none | Running, nothing needs you |
| grey ring | Starting |
| two bars | Paused (Deluno: automation; Weir: processing) |
| red dot | Needs you |
| blue dot | An update is ready |

- **The red dot means either the app is in trouble or something is waiting on you:**
  - the server stopped, or keeps failing;
  - something it relies on can't be reached (a download client, an indexer, Weir, Deluno);
  - items wait on you: Deluno's bell count, or Weir's files on hold or failed.
- **When two marks apply, one shows.** The order is red dot, then grey ring, then two bars, then blue dot.
- **There is no mark for being busy.** Downloading and processing are normal, and a mark that is on most of the day stops meaning anything.
- **How it is drawn:** at the shell's small-icon size, with a thin outline so it reads on light and dark taskbars. The icon file carries 16, 20, 24, 32, 40, 48, 64, 128 and 256 px frames.
- **When it appears:** at once, in the Starting state. Never only after the server is up.

## What the hover text says

`<App> - <status>`. When something needs you, it also says what, so the reason for the red dot is never hidden. Examples:

- `Deluno - Running at http://localhost:7879 - 2 things need a look`
- `Deluno - Paused`
- `Weir - Starting...`
- `Weir - Update ready (1.0.0-rc.10)`
- `Weir - Stopped - choose Restart Weir`

It is kept short but never cut mid-word. It changes as the state changes, with no timer.

## The menu

```
Open <App>                                   (bold; a left click does the same)
<status line, same words as the hover text>  (greyed out)
---
Pause automation / Resume automation         (Weir: Pause processing / Resume processing)
Restart <App>
---
Copy address
Allow other devices on your network...
Only allow this PC
Change port (<port>)...
---
Open data folder
Open logs folder
[x] Start with Windows
---
Check for updates                            (changes with the update, below)
<App> v<version>                             (greyed out)
Report a problem...
---
Quit <App>                                   (hover text says what it stops)
```

- **The update item:** `Check for updates`, then `Checking for updates...` (greyed out), then `Downloading update...` (greyed out), then `Download update (v<version>)`, then `Restart to update (v<version>)`.
- **Report a problem...** opens the logs folder while the repositories are private. Once they are public, it opens a new GitHub issue page with the version filled in. No paths or addresses ever go in the link.
- **Start with Windows** is asked once, at install or first run: "Start <App> when you sign in to Windows?", Yes or No. Nothing is on by default. The tick in the menu shows the current choice and flips it at any time, without closing the app.
- **Quit <App>** stops the app. Its hover text says so:
  - Deluno: "Stops Deluno and closes this icon. Download clients Deluno started keep running."
  - Weir: "Stops Weir after running jobs finish, then closes this icon. A downloaded update is installed."
- **There is no Stop or Start item.** Restart covers "it's misbehaving", and Quit covers "turn it off".

**Deluno-only lines:**
- `Add Transmission's torrents back...` and `Try again: install apps...` show at the top only while something is pending.
- `Run as a Windows service...` and `Let any address in...` sit in an `Advanced` submenu above Quit.

## Behaviour

- **Clicks:** a left click opens the app once, and so does a double-click. A middle click does nothing. If the app can't open yet, the click says why, in a balloon.
- **The address** the app opens is always the live one, not a saved setting.
- **A browser opens only when you click** something: the icon, a menu item or a balloon. Start-up, sign-in start, an update restart, a port change and a second launch never open one.
- **A second launch** shows a balloon from the running copy, "<App> is already running", with the address.
- **Balloons:**
  - a click does what the balloon offers: open Updates, Restart, open the logs folder, Try again;
  - Windows decides how long a notice stays: it ignores the time an app asks for. So a notice is never the only place a problem shows. Anything you must act on also shows in the red dot, the hover text and the menu's status line, until it is dealt with.
