# Uh FiveM Optimizer

**Tune your setup. See every change.** A small, open-source Windows desktop app by Uh for FiveM players.

> Version 0.3.1 adds a reversible low-end graphics preset, reviewed temporary-file cleanup, and an experimental importer for the linked community visual pack, plus a release-order fix for the updater. It does not measure game FPS or guarantee a performance increase. Test on a few PCs before recommending it to all players.

## Download

Get a Windows x64 preview from [GitHub Releases](https://github.com/OnniUh/uh-fivem-optimizer/releases). Each successful build on `main` publishes a separate prerelease tied to its source commit. Download `UhFiveMOptimizer.exe`, `SHA256SUMS.txt`, and `build-info.json` from the **same release**.

Run the EXE as a normal user. No installer or separate .NET installation is needed for the published self-contained EXE. The app is currently **unsigned**: it does not have a publisher code-signing certificate. It does not ask you to disable Windows security software.

Requires a currently supported x64 Windows 10/11 environment compatible with .NET 10. ARM64 and Windows 7/8 are not targeted. Keep Windows and your graphics driver updated through their official channels.

## Included in v0.3.1

- **Low-end graphics (GTA V Legacy)**: preview supported changes to FiveM's `gta5_settings.xml` in the recognized version-27 format, back up the complete original file, apply atomically, and verify. Reduces graphics quality, MSAA and draw distance; preserves unknown fields, GPU/monitor selection, refresh rate, controls and VSync. Resolution stays unchanged unless the optional cap of 1280×720 is selected; dimensions already smaller are preserved. Other settings formats are refused; use in-game graphics settings for Enhanced.
- **Temporary-file cleanup**: preview old files in user Temp, `%TEMP%` and Windows Temp. Only files created **and** modified more than seven days ago are candidates. A redirected `%TEMP%` must be a local folder named `Temp`; other redirected paths are excluded with a notice. No drive-root, network or linked roots. No recursive directory deletion.
- **Optional Prefetch cleanup**: unchecked and explicitly marked not recommended. Only old `.pf` files directly in the Prefetch folder; preserves ReadyBoot and other files. Prefetch helps launch performance; deleting it can slow subsequent launches. Cleanup frees disk space and is not an FPS guarantee.
- **Experimental visual-pack ZIP importer**: opens [the author's Cfx.re thread](https://forum.cfx.re/t/release-fivem-for-low-end-pc-s-2-0/46352), then previews a ZIP downloaded by the player. Accepts only `citizen/common/data/visualsettings.dat` and `citizen/common/data/timecycle/*.xml`; does not replace the whole citizen folder. Original files are backed up and newly introduced files are tracked for removal during restore. The original pack is **not bundled or automatically downloaded**. Its current compatibility and original ZIP have not been validated; loading issues are reported in the old thread. Follow your server's visual-modification rules.
- **File Recovery**: durable Prepared journal before target writes, verification, interrupted-batch recovery, separate graphics/visual backups, and preservation of later user/game edits when explicitly restoring over them.
- **Launch FiveM**: uses the located launcher and minimizes this window. No automatic deletion, app closing, or power changes during launch.

Both graphics actions require an explicit **GTA V Legacy** selection. [Cfx.re currently documents that Enhanced early access always enables pure mode and cannot use graphics mods](https://docs.fivem.net/docs/developers/legacy-vs-enhanced/). Legacy servers may also [enforce pure mode](https://docs.fivem.net/docs/server-manual/server-commands/#sv_purelevel-level). The app does not bypass these restrictions. Cleanup and power selection are independent of edition.

- Sylva-inspired native styling: glossy pill buttons, pointer highlights, and a vector ribbon sculpture. No browser or Three.js runtime is embedded.
- **Appearance**: six accent presets, custom six-digit hex colors, live previews, contrast-aware text, and saved preferences.
- **Motion controls**: reduced motion, Windows animation preferences, and default pause while FiveM runs. Artwork timers stop on hidden pages, inactive windows, and minimization.
- **Updates**: check the public GitHub release feed, download a newer version on request, verify it, replace the EXE in place, and restart. Valid cached downloads are reused.

- CPU, GPU, physical-memory, operating-system, and free-storage scan.
- Live whole-PC CPU/RAM charts, sampled every two seconds, with up to 60 samples.
- FiveM detection at its standard location, plus a manual folder picker.
- Bounded, read-only cache size inspection. No game/cache files are deleted.
- Selection of **existing** Windows power plans with an explicit before/after review.
- An atomic, on-disk backup saved **before** changing the power plan, followed by verification.
- Restoration after closing/reopening the app or restarting Windows.
- Windows Settings shortcuts for Game Mode, graphics preferences, background captures, and startup apps.
- User-initiated JSON diagnostic export.

All tuning and deletion require a review. FiveM and GTA V must be closed before graphics/visual changes, file restoration, or cleanup. Cleanup skips locked, changed, protected, linked and hard-linked files and uses exclusive Windows file handles to verify each target before deletion. It never requests elevation. Temp deletion is permanent and has **no backup or recycle-bin undo**. The app does not alter antivirus, services, anti-cheat, registry settings, process priorities, network settings, or hidden power plans. Windows Settings changes remain outside the app's backup.

## How to use

1. Open the app and review **Overview**. Scan again or choose **Locate FiveM** if necessary.
2. Open **Windows tuning**, choose a plan, and click **Review change**. Read the exact change and confirm if you want it.
3. Closing the app leaves the chosen plan active. Use **Restore** to return to your saved original plan.
4. To try a different plan, restore the outstanding backup first.
5. In **Appearance**, preview a preset or custom color and choose **Save appearance**. Use reduced motion or keep the default pause during FiveM sessions.
6. In **Updates**, choose **Check for updates**. If a newer compatible version is available, choose **Download & restart** and confirm.
7. Close FiveM and GTA V. In **Optimizations**, preview the low-end preset. Check the optional resolution reduction only if you want a softer image. Review each change and apply. Use **Recovery** to restore the original graphics file.
8. For cleanup, select folders and preview. Review the candidate paths and permanent-deletion warning before deleting. Canceled cleanup keeps already-deleted files deleted; remaining files are untouched.
9. To try the legacy visual pack separately, download its ZIP from the original thread and choose **Preview downloaded ZIP**. Only the supported archive layout is accepted. Restore that pack if loading or visuals break.
10. Launch FiveM and compare the same server/scene under similar player load. Graphics reductions help rendering bottlenecks; server scripts, CPU limits and assets can dominate. Moving from 30–40 FPS to 80–90 FPS is **not a promised outcome**.

**Install 0.3.1 manually once if you have 0.1.0, 0.2.0 or 0.3.0.** Earlier update checkers assumed newest-first release ordering, and can miss newer versions on the current feed. Version 0.3.1 scans the entire bounded page and chooses the largest compatible numeric version regardless of order. Subsequent newer versions can install through Updates. Each new version still requires one complete EXE download; checking an installed version does not redownload it. Canceled partial downloads are discarded; completed verified downloads can be reused for installation retries.

Higher-performance power plans can increase heat, fan noise, and battery use. Some PCs expose only one plan or use Windows Power mode instead. The app respects the plans that Windows exposes. A server with expensive scripts or assets can remain slow regardless of PC settings.

## Privacy and trust

The app has no analytics, account system, advertising, scheduled update checker, or background service. Reads, appearance, and backups remain local. **Only user-initiated update checks and downloads contact GitHub and its release-asset CDN**, using HTTPS and a product user-agent, without an account token or diagnostic upload. Settings and release links open Windows Settings or your browser. A diagnostic report is saved only to a location you choose and can contain your Windows username in installation paths; review it before sharing.

App state is under `%LOCALAPPDATA%\UhFiveMOptimizer`:

| File | Purpose |
| --- | --- |
| `power-backup.json` | Original and target plan GUIDs, names, timestamps, and recovery status |
| `fivem-path.txt` | A manually selected FiveM installation folder |
| `appearance.json` | Saved accent color and motion preferences |
| `updates/<sha256>/` | Verified update cache and temporary installer handoff files |
| `graphics/files-backup.json` | Original graphics file bytes, original/replacement hashes and recovery state |
| `visual-pack/files-backup.json` | Original visual files, introduced-file records and recovery state |
| `graphics/` or `visual-pack/before-restore-*.json` | Later edits preserved when explicitly restoring over changed files |

The updater retains the previous executable beside the installed EXE as `<app.exe>.previous`. A failed process launch restores it automatically. If a newly launched version later crashes, close it and restore the previous file manually; a successful process start is not a full application health check. Updates require write access to the installation directory and do not request administrator elevation.

Read [SECURITY.md](SECURITY.md) for recovery and verification details. There has been no independent security audit. Public source and a checksum do not alone prove an executable is safe. The published build workflow, release commit, and build metadata provide an inspectable chain; bit-for-bit reproducibility is not claimed.

Verify a downloaded EXE with PowerShell:

```powershell
Get-FileHash .\UhFiveMOptimizer.exe -Algorithm SHA256
```

Compare the hash with `SHA256SUMS.txt` from that release. You can also inspect the [build workflow](.github/workflows/build.yml) and [source](src).

## Build from source

Install the official .NET 10 SDK matching `global.json` on Windows. There are no third-party application or test-package dependencies. Self-contained publishing downloads Microsoft's runtime packs and the SDK's single-file build tools from NuGet.

```powershell
dotnet restore UhOptimizer.slnx --configfile NuGet.Config
dotnet build UhOptimizer.slnx -c Release --no-restore
dotnet run --project tests/UhOptimizer.Tests -c Release --no-build
pwsh -File ./build.ps1
```

`build.ps1` runs the build and behavioral tests, then publishes the Windows x64 EXE, SHA-256 checksums, and build metadata in `artifacts/`. GitHub Actions runs the same script on Windows. The workflow pins external actions to commit SHAs.

For a read-only UI smoke test (no Windows settings are changed):

```powershell
./src/UhOptimizer.App/bin/Release/net10.0-windows/UhFiveMOptimizer.exe --smoke-test ./artifacts/ui-smoke
```

The smoke test renders seven pages at normal and minimum sizes, exercises color persistence/invalid input and motion timer behavior, and applies/restores a disposable graphics fixture. It writes only beneath the selected smoke-test folder; real cleanup is disabled in smoke mode. It does not change real game settings, user preferences or Windows settings. Results contain machine details and should be reviewed before sharing. Behavioral tests use fake power/HTTP providers and disposable file fixtures. Junction and hard-link tests are mandatory in CI; local sandboxes that forbid creating them report explicit skips.

The workflow also checks live GitHub release metadata without downloading an EXE and runs `scripts/Test-UpdateInstaller.ps1` against a disposable copy of the actual published EXE. That probe exercises the updater helper, parent exit, atomic replacement, backup, and restart without replacing a user's installed app.

## Project layout

```text
src/UhOptimizer.App/       WPF desktop UI and Windows API integration
src/UhOptimizer.Core/      Recovery journal and bounded FiveM path/cache inspection
tests/UhOptimizer.Tests/   Dependency-free behavioral test runner
.github/workflows/        Windows build, test, artifact, and preview-release workflow
docs/                     Release notes and manual validation checklist
```

## Current limits

This version does not include in-game frame-time capture, FiveM cache deletion, driver installation, overlays, or unattended updates. Graphics edits and the original visual pack require pilot testing with current FiveM builds and the target server; automated fixtures prove file handling, not game compatibility or FPS improvements. Real power-plan apply/restore should also be validated on a pilot PC; automated tests simulate those writes. Update checks choose the largest compatible numeric version in the first 20 releases returned by GitHub, without relying on release order; older pages are not searched. Bump the numeric project version when publishing an update. A release with the same version is not offered again.

## References

- [Microsoft power-management APIs](https://learn.microsoft.com/en-us/windows/win32/power/power-management-functions)
- [Microsoft Windows Settings URIs](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings)
- [Microsoft .NET single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- [Official FiveM installation guide](https://docs.fivem.net/docs/client-manual/installing-fivem/)
- [FiveM graphics/profile implementation](https://github.com/citizenfx/fivem/blob/master/code/components/gta-core-five/src/GameProfileSettings.cpp)
- [Microsoft App Launch Prefetch behavior](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/ns-processthreadsapi-override_prefetch_parameter)

Independent community tool. Not affiliated with Cfx.re, Rockstar Games, or Microsoft. MIT licensed.
