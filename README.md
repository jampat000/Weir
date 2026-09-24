<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="packaging/brand/weir-mark.svg">
    <img src="packaging/brand/weir-mark-light.svg" alt="Weir" width="96">
  </picture>
</p>

<h1 align="center">Weir</h1>

<p align="center">
  <b>Keep the audio and subtitle tracks you want. Drop the rest.</b><br>
  A self-hosted app that cleans up your movie and TV files: new downloads, and the library you already have.
</p>

<p align="center">
  <a href="https://github.com/jampat000/Weir/actions/workflows/ci.yml"><img alt="Test" src="https://github.com/jampat000/Weir/actions/workflows/ci.yml/badge.svg"></a>
  <a href="https://github.com/jampat000/Weir/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/jampat000/Weir?label=release"></a>
  <a href="https://github.com/jampat000/Weir/pkgs/container/weir"><img alt="Docker" src="https://img.shields.io/badge/docker-ghcr.io%2Fjampat000%2Fweir-blue"></a>
  <a href="LICENSE"><img alt="License: AGPL-3.0" src="https://img.shields.io/badge/license-AGPL--3.0-green"></a>
</p>

<p align="center">
  <a href="#install-with-docker">Docker</a> ·
  <a href="#install-on-windows">Windows</a> ·
  <a href="#first-steps">First steps</a> ·
  <a href="https://jampat000.github.io/Weir/">Documentation</a>
</p>

<!-- README_LOCKED_SECTION_START: project-note -->
## A note on this project

Weir started as a tool for my own library. I wanted every file to keep only the audio and subtitle tracks I actually use, and nothing I tried did that well, especially for a library I already had.

I'm not a software engineer, so I'll be upfront about it: Weir is built with AI coding assistants. I've tried to make up for that with process. Every change goes in through a pull request that has to pass the full test suite (unit, API contract and end-to-end tests), and a release only goes out once those tests pass on the exact code being shipped. Weir also works on a copy of each file and only replaces the original once the new one checks out, so a bug shouldn't cost you anything in your library.

It's opinionated. Everything in it is there because it fixed a real problem in my own setup first. If it suits the way you manage your library too, use it, improve it, and share what you change under the same license.

— James

<!-- README_LOCKED_SECTION_END: project-note -->

## What Weir does

Movie and TV files often come with a dozen audio tracks and subtitles in languages you'll never use.
Weir removes them, so every file ends up with just the tracks you chose.

- **You set the rules once.** For example: keep English and Japanese audio, keep English subtitles, drop commentary.
- **New downloads are cleaned automatically.** Weir watches a folder, cleans each file that lands there, and puts the result in an output folder.
- **Your existing library can be cleaned too.** Weir scans it, shows you what it would remove and how much space that saves, and only removes anything once you confirm.
- **It never re-encodes.** Tracks are copied as they are, so there's no quality loss and it's fast.
- **Nothing is lost if something goes wrong.** Weir works on a copy and only replaces a file once the new one checks out.

Beyond the cleaning itself:

- **History** keeps a record of every file Weir handled: which tracks it kept and removed, and why a file was held or skipped.
- **Library** lists the files already on your storage, library by library. You can open any file and choose its tracks yourself when the rules don't fit it.
- **Schedules** set the hours each library may start work, so cleaning a large library can wait for the night.
- **Alerts** go to Discord or any webhook when a file finishes or fails.
- **Media managers** work alongside Weir. Deluno hands files over and imports them once they're clean. Sonarr and Radarr import from Weir's output folder, and Weir checks their queues before it touches a file.

Everything Weir needs comes with it, including ffmpeg and MKVToolNix. There's nothing else to install.

## Requirements

- Windows 10 or 11 (64-bit, x64), or
- any 64-bit Linux machine or NAS with Docker, on Intel/AMD (amd64) or ARM (arm64).

## Screenshots

| Processing: every file as it is worked on | History: what Weir did to a file, track by track |
| --- | --- |
| ![Processing](docs/assets/screenshots/processing.png) | ![History of one file](docs/assets/screenshots/history-detail.png) |

| History | Library: the files already on your storage |
| --- | --- |
| ![History](docs/assets/screenshots/history.png) | ![Library](docs/assets/screenshots/library.png) |

| Settings › Rules | Settings › Schedule |
| --- | --- |
| ![Rules](docs/assets/screenshots/settings.png) | ![Schedule](docs/assets/screenshots/schedule.png) |

| Light mode | On your phone |
| --- | --- |
| ![Processing in light mode](docs/assets/screenshots/processing-light.png) | ![Processing on a phone](docs/assets/screenshots/processing-mobile.png) |

## Install with Docker

Weir runs on any machine with Docker, including Synology, Unraid, TrueNAS and Raspberry Pi. Both
64-bit Intel/AMD and ARM are supported.

### The quickest way

Make a folder, save this as `compose.yaml` inside it:

```yaml
services:
  weir:
    image: ghcr.io/jampat000/weir:latest
    container_name: weir
    ports:
      - "9347:9347"
    volumes:
      - ./weir-data:/data/weir
    restart: unless-stopped
```

Then run:

```bash
docker compose up -d
```

Open **http://your-server-ip:9347** and create your account. That's it.

`./weir-data` holds Weir's database, settings, logs and backups. Keep it and you keep everything.

### With your media folders

Weir can only clean files it can see, so give it your media folders. This is the setup most people want:

```yaml
services:
  weir:
    image: ghcr.io/jampat000/weir:latest
    container_name: weir
    ports:
      - "9347:9347"
    environment:
      - WEIR_PUID=1000   # the user that owns your media folders
      - WEIR_PGID=1000   # that user's group
    volumes:
      - ./weir-data:/data/weir
      - /srv/media:/media
    restart: unless-stopped
```

- **`/srv/media`** is where your media lives on the host. Change it to your own path.
- **`/media`** is where Weir sees it. Use `/media/...` paths when you set up folders in Weir.
- **`WEIR_PUID` / `WEIR_PGID`** make Weir read and write files as that user, so it can move them. Run `id your-username` on the host to find the numbers. On Synology it's usually `1026` / `100`, on Unraid `99` / `100`.

### Alongside Sonarr, Radarr and a download client

If Weir works next to other apps, **give every container the same folders at the same paths**.
When one app tells another where a file is, that path has to mean the same thing in both.

```yaml
services:
  weir:
    image: ghcr.io/jampat000/weir:latest
    container_name: weir
    ports:
      - "9347:9347"
    environment:
      - WEIR_PUID=1000
      - WEIR_PGID=1000
    volumes:
      - ./weir-data:/data/weir
      - /srv/media:/media
    restart: unless-stopped

  sonarr:
    image: lscr.io/linuxserver/sonarr:latest
    environment:
      - PUID=1000
      - PGID=1000
    volumes:
      - ./sonarr-config:/config
      - /srv/media:/media        # same folder, same path as Weir
    ports:
      - "8989:8989"
    restart: unless-stopped

  qbittorrent:
    image: lscr.io/linuxserver/qbittorrent:latest
    environment:
      - PUID=1000
      - PGID=1000
    volumes:
      - ./qbittorrent-config:/config
      - /srv/media:/media        # same folder, same path again
    ports:
      - "8080:8080"
    restart: unless-stopped
```

A folder layout that works well:

```text
/srv/media
├── downloads/complete/movies   ← your download client finishes files here   (Weir's watched folder)
├── downloads/complete/tv
├── weir/movies                 ← Weir puts cleaned files here               (Weir's output folder)
├── weir/tv
├── movies                      ← your library
└── tv
```

### Common changes

| I want to… | Do this |
| --- | --- |
| Use a different port | Change the left number: `"8080:9347"` puts Weir at `http://your-server-ip:8080` |
| Pin a version instead of `latest` | `image: ghcr.io/jampat000/weir:3.2.5` |
| Use HTTPS through a reverse proxy | Set `WEIR_TRUSTED_PROXY_IPS=<your proxy's IP>`. The sign-in cookie becomes HTTPS-only on its own once requests arrive over HTTPS; set `WEIR_SESSION_COOKIE_SECURE=true` only to force it. See [the reverse proxy guide](https://jampat000.github.io/Weir/docs/deployment/reverse-proxy) |
| Protect saved API keys with their own secret | Set `WEIR_CREDENTIALS_SECRET` to a long random value (`openssl rand -hex 32`) **before** you add Sonarr or Radarr |
| Use a GPU | See [hardware acceleration](docker/README.md#hardware-acceleration-and-device-passthrough). It's optional; Weir doesn't re-encode, so you usually don't need it |

You don't need an `.env` file or any secrets to get started. Weir creates its own session secret
on first start and keeps it in `weir-data`.

Every Docker option is in [docker/README.md](docker/README.md).

## Install on Windows

1. Download **`Weir-win-Setup.exe`** from the [latest release](https://github.com/jampat000/Weir/releases/latest).
2. Run it. You don't need admin rights.
3. Weir asks which port to use the first time it starts. Keep **9347** unless something else uses it.
4. Your browser opens Weir. Create your account.

Weir lives in the system tray, next to the clock. Right-click the icon to open Weir, open its data
folder, change the port, check for updates or quit.

- Weir is installed to `%LocalAppData%\Weir`. Your data (database, logs, backups) is kept in `C:\ProgramData\Weir`.
- Weir runs as you, not as a Windows service, so it can reach your mapped network drives and NAS shares.
- Installing without a screen (a script, or remotely)? Pass the port: `Weir-win-Setup.exe -- --port 9347`.

More in the [Windows guide](https://jampat000.github.io/Weir/docs/deployment/windows).

## First steps

1. **Create your account.** The first time you open Weir, it asks for a username and password. This is the admin account.
2. **Follow the setup wizard.** Pick your time zone, then give Weir two folders for Movies and two for TV:
   - **Watched folder**: where your downloads finish. Weir cleans whatever lands here.
   - **Output folder**: where Weir puts each cleaned file.

   You can skip the wizard and add libraries later under **Settings › Libraries**.
3. **Choose what to keep.** Under **Settings › Rules**, set the audio and subtitle languages each library keeps.
4. **Try it.** Put a file in a watched folder. Weir usually notices within seconds. On network shares
   and in Docker it can take up to five minutes, because Weir falls back to checking on a timer.
   The file shows up on **Processing** while it's being worked on, and in **History** once it's done.

To clean a library you already have, open **Library**, pick the library from the title and press
**Check again**. Weir shows you what it would remove and how much space that frees before it changes
anything.

### Connecting other apps

Under **Settings › Media managers** you can connect:

- **Deluno** hands each file to Weir, waits for it to be cleaned, then imports it. This is the fully automatic setup.
- **Sonarr and Radarr** import the cleaned files from Weir's output folder. Weir also checks their
  queue before it touches a file, and tells them to re-read a file after it cleans that file in
  your library. See [Sonarr and Radarr](#sonarr-and-radarr) below.
- **Anything else** can hand files to Weir by posting to `/api/v1/intake/webhook/native`. The [API reference](https://jampat000.github.io/Weir/docs/api/) links the full specification.

### Sonarr and Radarr

Your download client finishes into Weir's watched folder, Weir writes the cleaned copy to its output
folder, and Sonarr or Radarr import from there. You connect them with a **remote path mapping**: it
tells Sonarr "when the download client says a file is in the downloads folder, look in Weir's output
folder instead." Sonarr then only ever sees cleaned files.

1. **In Weir**, open **Settings › Libraries** and edit the library.
   - **Watched folder**: where your download client finishes files, e.g. `/media/downloads/complete/tv`
   - **Output folder**: where Weir puts cleaned files, e.g. `/media/weir/tv`
   - Using torrents? Turn **After cleaning, remove the original download** off, so the torrent keeps
     seeding. Your download client or Sonarr removes it later, as they normally would.
2. **Connect Sonarr** under **Settings › Media managers**, with its address and API key
   (Sonarr shows its API key on its **General** settings page).
3. Back in the library, the **Media manager** section shows the exact mapping to add, with copy buttons.
4. **In Sonarr**, go to **Settings › Download Clients › Remote Path Mappings** and press **+**:
   - **Host**: exactly what's in your download client's **Host** field on that same screen, e.g. `qbittorrent`
   - **Remote Path**: Weir's watched folder, e.g. `/media/downloads/complete/tv/`
   - **Local Path**: Weir's output folder, e.g. `/media/weir/tv/`

   The output folder has to exist before Sonarr will save the mapping.
5. Keep **Completed Download Handling** switched on in Sonarr. This setup relies on it.
6. Back in Weir, press **Check again** in the library's **Media manager** section. It reads Sonarr's
   settings (it never changes them) and shows ✓, or tells you exactly what to fix.

Radarr works the same way, with its own movies folders. After a download finishes, Sonarr shows
"No files found are eligible for import" until Weir is done with it. That's normal: it checks again
every minute and imports the cleaned file as soon as it appears.

## Updating

- **Docker:** `docker compose pull && docker compose up -d`. Your data carries over.
- **Windows:** right-click the tray icon and choose **Check for updates**, or go to **System › About** in Weir.

Weir updates its database itself when it starts. Before a big upgrade, it's worth taking a backup
in **System › Backups**.

What changed in each version: [release notes](https://github.com/jampat000/Weir/releases).

## Something not working?

| Problem | Try this |
| --- | --- |
| Files sit in the watched folder and nothing happens | Check the path in Weir is the path **inside the container** (`/media/...`, not `/srv/media/...`). In Docker, Weir may take up to five minutes to notice a file. |
| "Permission denied" in a file's History | Set `WEIR_PUID` / `WEIR_PGID` to the user that owns your media folders |
| Can't open Weir | Check the container is running (`docker ps`) and you're using the right port. Weir's health check is at `http://your-server-ip:9347/health` |
| Logged out after every restart | You're setting `WEIR_SESSION_SECRET` to a different value each time. Remove it and let Weir manage it |

Still stuck? [Open an issue](https://github.com/jampat000/Weir/issues). Include what you expected,
what happened, and the lines from **System › Logs**. [SUPPORT.md](SUPPORT.md) lists what else helps.

Found a security problem? Don't open a public issue. Report it privately as described in
[SECURITY.md](SECURITY.md).

## Documentation

- [Documentation site](https://jampat000.github.io/Weir/): installing, reverse proxies, security, the API
- [Docker reference](docker/README.md): every variable, GPUs, file ownership, network shares
- [Changelog](CHANGELOG.md): one line per version, linked to the full release notes

## Building from source

For developers. You need the .NET 10 SDK and Node.js 24.

```bash
git clone https://github.com/jampat000/Weir.git
cd Weir/apps/web
npm ci
npm run dev
```

This starts the server and the web app together at `http://localhost:8782/`. See
[local development](docs/local-development.md), [contributing](CONTRIBUTING.md) and
[how releases are made](docs/release.md).

## Support Weir

Weir is free. If it saves you time, the best ways to help are to star the repository, report bugs
you find, and share improvements. You can also sponsor it through
[GitHub Sponsors](https://github.com/sponsors/jampat000).

## License

Weir is licensed under the [GNU Affero General Public License v3.0 or later](LICENSE).
You can use, study, change and share it. If you share a changed version, or run one as a service
for others, you have to make its source available under the same license.
