# WP7 — Packaging, CLI & docs

* Branch: `wp7-packaging`
* Scope: `packaging/` (Arch PKGBUILD, Flatpak draft, desktop file, AppStream
  metainfo, MIME xml, icons, `sharex` wrapper), `scripts/linux/publish.sh` +
  `scripts/linux/install-user.sh`, the self-contained `dotnet publish -r linux-x64`
  profile, `README.linux.md`, `docs/CLI.md`.
* Version packaged: **21.0.0** (`Directory.build.props`).
* Environment used for verification: Arch-family Linux, Hyprland (Wayland) +
  XWayland, .NET SDK 10.0.402 in `~/.dotnet`, `magick`, `appstreamcli`,
  `desktop-file-validate`, `xmllint`, `makepkg`, `fakeroot`, `bwrap` available;
  **no** `flatpak-builder`, `shellcheck`, `inkscape`, `icotool`.

Legend: **works** / **partial** / **missing** / **unverified**.

## Deliverables

| File | What it is |
| --- | --- |
| `packaging/arch/PKGBUILD` | Arch/CachyOS package, builds the checkout with `dotnet publish` |
| `packaging/flatpak/io.github.ShareX.ShareX.yml` | Flatpak manifest **draft** (not build-tested) |
| `packaging/linux/sharex` | launcher installed as `<prefix>/bin/sharex` |
| `packaging/linux/sharex.desktop` | desktop entry (menu, MIME, desktop actions) |
| `packaging/linux/sharex.xml` | shared-mime-info for `.sxcu` / `.sxie` |
| `packaging/linux/io.github.ShareX.ShareX.metainfo.xml` | AppStream metadata |
| `packaging/linux/icons/hicolor/**` | 16/24/32/48/64/128/256 PNG + `scalable` SVG |
| `packaging/linux/generate-icons.sh` | regenerates the icon set from `ShareX_Icon.ico` |
| `scripts/linux/publish.sh` | self-contained single-file publish wrapper |
| `scripts/linux/install-user.sh` | idempotent per-user install into `~/.local` |
| `ShareX/Properties/PublishProfiles/linux-x64.pubxml` | the publish profile itself |
| `README.linux.md` | install/build/run, first run, keybindings, support matrix, troubleshooting |
| `docs/CLI.md` | every `sharex -<action>`, launcher options, per-desktop keybinding examples |
| `.gitignore` | ignores `/dist/` and makepkg leftovers |

## Feature verification

| Feature | Status | How verified | Known gaps |
| --- | --- | --- | --- |
| Self-contained publish profile (`linux-x64.pubxml`) | **works** | `scripts/linux/publish.sh` → `dist/linux-x64/ShareX`, one 60 MB ELF ("stripped", self-contained single file). Ran it with an isolated HOME: a real `ShareX | Welcome to ShareX` window appeared (`hyprctl clients`), a `grim` crop was 7796 colours, grayscale stddev 0.156 (real content, not a blank frame). | Single-file build extracts native libs to `~/.net/ShareX/<hash>/` on first run (honours `$HOME`). |
| Publish profile emits no debug symbols | **works** | `publish.sh` passes `-p:DebugType=none -p:DebugSymbols=false` (the profile only covers the main project, so referenced projects would otherwise drop PDBs); re-published output contains **0** `.pdb`. | Use `--symbols` to keep them. |
| `install-user.sh` install | **works** | Ran with `HOME=/tmp/sx-wp7-install` (throwaway): installs `~/.local/bin/sharex`, `~/.local/lib/sharex/*`, desktop file, metainfo, MIME xml, all icons. | — |
| `install-user.sh` idempotency | **works** | Ran twice; second run exits 0 and leaves a valid install (`version` file still correct). | — |
| `install-user.sh --uninstall` | **works** | Removed every installed path; `update-desktop-database`, `update-mime-database`, `gtk-update-icon-cache` refreshed so the compiled `mime.cache`, `mimeinfo.cache` and `icon-theme.cache` contain **0** references to ShareX. Leaves the personal folder untouched. | — |
| `sharex` wrapper: `--version` / `-h` | **works** | `sharex --version` → `ShareX 21.0.0 (Linux port)` (reads `../lib/sharex/version`); `-h` prints the usage summary. | — |
| `sharex` wrapper: MIME translation | **works** | `SHAREX_BIN=/usr/bin/echo sharex /tmp/a.sxcu /tmp/b.sxie /tmp/c.png` → `-CustomUploader /tmp/a.sxcu -ImageEffect /tmp/b.sxie /tmp/c.png`; a `.sxcu` used as a **parameter** of a `-command` is left alone (`-task /tmp/looks.sxcu /tmp/real.sxcu` → `-task /tmp/looks.sxcu -CustomUploader /tmp/real.sxcu`). | — |
| `sharex` wrapper: binary not found | **works** | Running the wrapper from a directory with no `lib/sharex` prints the lookup paths and exits **127**. | — |
| Launcher resolution for all layouts | **works** | `/usr/bin`+`/usr/lib`, `~/.local/bin`+`~/.local/lib`, `/app/bin`+`/app/lib` all resolve through `dirname(bindir)/lib/sharex`; `SHAREX_BIN` / `SHAREX_LIB` override. | — |
| CLI reference completeness | **works** | Programmatically diffed `docs/CLI.md` against the `HotkeyType` enum (`ShareX/Enums.cs`): all **82** actions present, none extra. Global flags/import commands/`-task`/`-workflow` taken from `Program.cs`, `ApplicationCommandLine.cs`, `PersonalPathManager.cs`, `ShareXCLIManager.cs`. | CLI.md groups actions by category (as the app's hotkey list does), so its order differs from the enum declaration order near the end (`AnimatedGifTrimmer`); the set is identical. |
| CLI flag parsing actually happens | **works** | Published binary `-sandbox -silent -NoHotkeys` logged `Flags: Dev, SilentRun, Sandbox, IgnoreHotkeyWarning` in the log. | — |
| `.desktop` entry | **works** | `desktop-file-validate` clean; `StartupWMClass=ShareX` matches the real WM class seen via `hyprctl`; desktop actions use real enum actions. | — |
| MIME xml | **works** | `xmllint --noout` clean; `update-mime-database` compiles it (checked in `install-user.sh` test). | — |
| AppStream metainfo | **works** | `appstreamcli validate --no-net` → *Validation was successful: pedantic: 1*. | Pedantic only: `cid-contains-uppercase-letter` (the upstream `io.github.ShareX` id has uppercase letters — accepted). Screenshots point at the upstream PNG because no Linux screenshots are hosted yet (they also cannot be visually reviewed in this environment). |
| Icons | **works** | All seven PNG sizes verified 16/24/32/48/64/128/256 with `magick identify`; master is the largest frame of `ShareX/ShareX_Icon.ico` (frame 0, 256×256) with Lanczos downscales. | `scalable/apps/sharex.svg` is the 256×256 raster wrapped in SVG (upstream ships no vector logo). |
| Arch package builds | **works** | `cd packaging/arch && makepkg -f --nodeps` → `sharex-linux-21.0.0-1-x86_64.pkg.tar.zst`. Contents verified: `/usr/bin/sharex`, `/usr/lib/sharex/{ShareX,version,host-manifest-*.json,ShareX_File_Icon.ico,…}`, desktop, metainfo, mime, all hicolor icons, license. `depends` derived from the published binary's real `ldd`/maps; `optdepends` list the runtime tools. | `arch=('x86_64' 'aarch64')` is declared but only `x86_64` was built and run. |
| Packaged binary runs | **works** | Extracted the package: `usr/bin/sharex --version` → `ShareX 21.0.0 (Linux port)`; MIME translation OK; the packaged `ShareX` itself started and rendered `Welcome to ShareX`. | — |
| `PKGBUILD` metadata/syntax | **works** | `makepkg --printsrcinfo` OK; `.PKGINFO` inspected. | — |
| Flatpak manifest | **unverified** | YAML parses (PyYAML); every install command/name checked by hand against the Arch/`~/.local` layout; `install -d` ordering fixed so desktop/metainfo dirs exist before they are written. | Cannot be build-tested: **no `flatpak-builder`** in the environment, and the .NET SDK archive **sha256 is still a TODO** in the manifest (and there is no aarch64 archive yet). |
| `README.linux.md` support matrix | **partial** | Hyprland column exercised in the port's environment; the mechanism for every other cell is named and implemented by the port. | sway / GNOME / KDE / X11 columns are **design targets** (🟢) until the lead's `docs/status/QA.md` runs them. The app is **X11-only today**, so every Wayland session runs through XWayland. |
| Per-desktop keybinding examples | **works** | Hyprland (`hyprland.conf`), sway (`sway` config), KDE (System Settings + portal), GNOME (`gsettings` custom-keybindings) and X11 (`XGrabKey`) documented in both `README.linux.md` and `docs/CLI.md`. | Examples not executed (would modify the live desktop config, which is out of bounds). |

## Known gaps and cross-package concerns

These were found while packaging; the fixes belong to other work packages (noted so
the lead can route them).

1. **The app has no XDG support and no working sandbox (WP1).** `AppPaths.PersonalFolder`
   is `Environment.SpecialFolder.MyDocuments + "/ShareX"`, which on this .NET runtime is
   the **real** user's `~/Documents/ShareX`; it is **not** affected by `HOME`,
   `XDG_CONFIG_HOME` or `XDG_DATA_HOME`. Consequences:
   * `scripts/linux/run.sh`'s `SX_SANDBOX` and the `-sandbox` flag do **not** isolate the
     personal folder — logs and (for normal runs) settings/history are written to the real
     `~/Documents/ShareX`.
   * When `HOME` is overridden to a directory without `.config/user-dirs.dirs`, .NET returns
     an **empty** `MyDocuments`, so `PersonalFolder` becomes the relative string `ShareX`
     and ShareX writes a `ShareX/` directory into the **current working directory** (this is
     how a stray `ShareX/Logs/` + `ShareX/History.db` appeared in the repo root; they were
     removed before committing).
   * Verified with a tiny .NET program: `HOME=/tmp/x dotnet t.dll` prints
     `MyDocuments=~/Documents`, `LocalApplicationData=~/.local/share`.
   * Workaround used for WP7 verification: run a copy of the binary named something other
     than `ShareX` with `-portable` (personal folder lands next to the executable) — that
     keeps the real home untouched.
   * Suggested fix (WP1): put settings/history/logs under `XDG_CONFIG_HOME`/`XDG_DATA_HOME`
     and make `-sandbox` (and/or an `XDG_*`-based personal path) actually isolated.

2. **`-portable` is broken for the packaged layout (WP1).** `PortablePersonalFolder` is a
   `ShareX/` folder next to the executable, but on Linux the executable is itself named
   `ShareX`, so the directory collides with the file. Reproduced with `bwrap` and a fake
   home: `ShareX -portable` shows a modal **`ShareX – Error`** dialog and never creates the
   folder. It works only when the executable is not called `ShareX` (verified with a renamed
   copy: a `ShareX/` folder with `Logs/`, `History.db`, `Screenshots/` is created and the
   real home stays untouched). Documented as a caveat in `README.linux.md` and `docs/CLI.md`.

3. **`-sandbox` still logs into the personal folder.** `SettingManager` correctly returns
   `null` for the config paths and `AppPaths.HistoryFilePath` is `null`, but
   `AppPaths.LogsFolder` is unconditional, so log lines are appended to
   `~/Documents/ShareX/Logs/ShareX-Log-<yyyy-MM>.txt`. Documented.

4. **The app renders through Avalonia's X11 backend (WP-SC/WP1).** `Avalonia.Wayland.dll` is
   not referenced by the project, so even on Wayland it runs as an XWayland client: with
   `env -u DISPLAY` it fails with `System.Exception: XOpenDisplay failed` (exit 134). The
   terminate-and-stay-resident UI, tray, notifications, clipboard and every capture path
   therefore depend on XWayland today. The Flatpak manifest keeps `--socket=x11` (not
   `fallback-x11`) for this reason.

5. **Startup logs two harmless `PlatformNotSupportedException`s** (Windows registry,
   named-pipe ACLs) before the UI starts. Cosmetic, but they will look alarming in issue
   reports; consider guarding them (WP1).

6. **aarch64 is declared but unverified** in the PKGBUILD (`-r linux-arm64`). No aarch64
   machine was available.

7. **No shell linter.** `shellcheck` is not installed; the wrapper and scripts were checked
   with `bash -n` only.

## Reproduce the verification

```bash
source scripts/linux/env.sh
scripts/linux/publish.sh                                   # -> dist/linux-x64/ShareX
scripts/linux/publish.sh --symbols                         # (optional) keep PDBs

# package
cd packaging/arch && makepkg -f --nodeps && cd ../..
tar --zstd -tf packaging/arch/sharex-linux-*.pkg.tar.zst    # contents

# launcher + MIME translation
SHAREX_LIB=dist/linux-x64 packaging/linux/sharex --version
SHAREX_BIN=/usr/bin/echo packaging/linux/sharex a.sxcu b.sxie c.png

# per-user install (throwaway home)
HOME=/tmp/sx-home scripts/linux/install-user.sh --no-publish
HOME=/tmp/sx-home scripts/linux/install-user.sh --no-publish      # idempotent
HOME=/tmp/sx-home scripts/linux/install-user.sh --uninstall

# metadata
desktop-file-validate packaging/linux/sharex.desktop
appstreamcli validate --no-net packaging/linux/io.github.ShareX.ShareX.metainfo.xml
xmllint --noout packaging/linux/sharex.xml

# run the published build without touching the real home (see gap 1)
cp dist/linux-x64/ShareX /tmp/run/sharex-bin
HOME=/tmp/run/home timeout 20 /tmp/run/sharex-bin -portable -OpenMainWindow -NoHotkeys
```

## Summary

Packaging, the launcher, the per-user installer, the publish profile, the CLI reference and
the Linux README are complete and verified by running them: the self-contained binary starts
and renders a real window, the Arch package builds and runs, the install/uninstall round-trip
is clean, and all metadata validates. The open items are the untested Flatpak draft, the
unverified non-Hyprland matrix columns, and the pre-existing (other packages') issues around
the app's personal folder, `-portable` and the missing Wayland backend — all listed above.
