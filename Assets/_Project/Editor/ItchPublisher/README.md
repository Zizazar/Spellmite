# itch.io Publisher

Builds the game for every enabled platform and uploads each build to its own itch.io channel with
[butler](https://itch.io/docs/butler/), itch.io's official upload tool. butler only sends what
changed since the last upload, so updates are quick.

Open it from `Tools → Itch.io → Publisher`, or from the **itch.io** button in the main toolbar
(turn it on in the toolbar's ⋮ menu: `Itch.io → Publisher`).

## First-time setup (about 2 minutes)

1. **Settings tab → Install butler.** It downloads from itch.io's server into your user folder and
   is shared by all projects. If you already have butler (in PATH, or from the itch app), it's found
   automatically.
2. **itch.io tab → API key.** Create one at <https://itch.io/user/settings/api-keys> and paste it.
   It's saved in this computer's editor preferences, never in the project.
   Alternative: the **butler login** button (browser login).
3. **itch.io tab → Page URL.** Paste `https://you.itch.io/your-game`, or pick it from **Pick from my
   games**. No page yet? **Create a new project on itch.io** first (it can stay a draft).
4. **Builds tab.** Tick the platforms to build. Each target has a channel name; itch.io guesses the
   platform from it (`win`, `linux`, `mac`/`osx`, `android`), so the defaults are a good choice.
5. **Dashboard → Build & Publish.**

Web builds need a one-time step on itch.io after the first upload: Edit game → *Kind of project* =
HTML, tick *This file will be played in the browser* on the `html5` upload, set the viewport size
(shown on the Builds tab), and Save.

## Dashboard

- **Status strip**: butler, account, game page and version. Click a cell to open its settings.
- **Checks**: everything that would make a build or upload fail, most with a one-click fix.
- **Targets**: per platform, the last local build (version, size, age) and what's live on itch.io
  (version and build number from `butler status`). Per-target **Build**, **Upload**, and a ⋮ menu
  (build and publish just this one, dry run, run the local build, open its folder).
- **Build & Publish**: bump the version, build every enabled target, upload the successful ones.
  **Build only** and **Publish last builds** do one half. **Dry run** shows what would be uploaded
  without uploading.
- **Progress** with Cancel during uploads (also shown in Unity's status bar), and a **Log**.

You can keep working while it uploads. Script reloads are held back until the upload finishes so
they don't cut it off.

## What it does for you

- Builds into `Builds/<channel>/` (git-ignored), cleaning the folder first so no stale files ship.
- Builds the active platform first and switches the editor back afterwards.
- Never uploads Unity's `*_BurstDebugInformation_DoNotShip` and
  `*_BackUpThisFolder_ButDontShipItWithYourGame` folders. Add more ignore patterns on the itch.io tab.
- Skips the upload only when both the files and the version already match itch.io (`--if-changed`). A new version is always uploaded; the ⋮ menu also has a forced upload.
- Version = Player Settings → Version (so the game can show `Application.version`). It's bumped on
  every publish (patch by default; can be off, minor or major). Android `versionCode` too.
- Warns about WebGL compression without decompression fallback and fixes it in one click.
- Asks before uploading, beeps when done, can open the game page afterwards (Settings tab).
- Keeps a history of builds and uploads (History tab).

## Command line / CI

```
Unity -batchmode -quit -projectPath . -executeMethod GameJam.ItchPublisher.ItchPublisherCli.BuildAndPublish
```

Options: `-itchChannels windows,html5`, `-itchVersion 1.2.3`, `-itchBump none|patch|minor|major`,
`-itchGame user/game`, `-itchDryRun`, `-itchForce` (upload even if unchanged). Other entry points: `ItchPublisherCli.BuildOnly`,
`ItchPublisherCli.PublishLastBuilds`. Set the `BUTLER_API_KEY` environment variable on the build
machine. Exit codes: 0 OK, 1 build/upload failed, 2 configuration problem.

## Where things are stored

| What | Where | Shared? |
|---|---|---|
| Game page, targets, version and upload options | `ProjectSettings/ItchPublisher.asset` | Yes, commit it |
| Build/upload history, last results | `UserSettings/ItchPublisher.json` | No |
| API key, butler path, personal options | Editor preferences | No (this computer) |
| butler installed by the tool | `%LOCALAPPDATA%/ItchPublisher/butler` | No |

API key lookup order: the key in the itch.io tab → `BUTLER_API_KEY` → `butler login`.
