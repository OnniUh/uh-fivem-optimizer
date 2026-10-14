# Uh FiveM Optimizer

**Tune your setup. See every change.** A small, open-source Windows desktop app by Uh for FiveM players.

> Version 0.2.0 is an early preview with a redesigned native UI, saved colors, motion controls, and in-app update installation. It provides diagnostics and reversible Windows power-plan selection. It does not measure game FPS or guarantee a performance increase. Test it on a few PCs before recommending it to all players.

## Download

Get a Windows x64 preview from [GitHub Releases](https://github.com/OnniUh/uh-fivem-optimizer/releases). Each successful build on `main` publishes a separate prerelease tied to its source commit. Download `UhFiveMOptimizer.exe`, `SHA256SUMS.txt`, and `build-info.json` from the **same release**.

Run the EXE as a normal user. No installer or separate .NET installation is needed for the published self-contained EXE. The app is currently **unsigned**: it does not have a publisher code-signing certificate. It does not ask you to disable Windows security software.

Requires a currently supported x64 Windows 10/11 environment compatible with .NET 10. ARM64 and Windows 7/8 are not targeted. Keep Windows and your graphics driver updated through their official channels.

## Included in v0.2.0

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

For system tuning, only the active Windows power plan is changed after confirmation. Appearance saves affect the app's own files; installing an update replaces the app's EXE after confirmation. The app does not alter antivirus, Windows services, anti-cheat, game files, registry settings, process priorities, network settings, or hidden power plans. Windows Settings shortcuts open the corresponding page; settings changed there are not covered by the app's backup.

## How to use

1. Open the app and review **Overview**. Scan again or choose **Locate FiveM** if necessary.
2. Open **Tune your setup**, choose a plan, and click **Review change**. Read the exact change and confirm if you want it.
3. Closing the app leaves the chosen plan active. Use **Restore** to return to your saved original plan.
4. To try a different plan, restore the outstanding backup first.
5. In **Appearance**, preview a preset or custom color and choose **Save appearance**. Use reduced motion or keep the default pause during FiveM sessions.
6. In **Updates**, choose **Check for updates**. If a newer compatible version is available, choose **Download & restart** and confirm.

Version 0.1.0 does not contain the updater: install 0.2.0 once to gain the Updates page. Subsequent newer releases can install through the app. Each newer version still requires one complete EXE download; checking an installed version does not redownload it. Canceled partial downloads are discarded; completed verified downloads can be reused when installation is retried.

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

The smoke test renders six pages at normal and minimum sizes, exercises color persistence/invalid input and motion timer behavior, and writes screenshots and a scan result to the chosen folder. It writes appearance fixtures under that smoke-test folder; it does not change real user preferences or Windows settings. Results contain machine details and should be reviewed before sharing. Behavioral tests use fake power and HTTP providers.

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

This version does not include in-game frame-time capture, graphics-preset editing, cache cleaning, driver installation, overlays, or unattended updates. Real power-plan apply/restore should be validated on a pilot PC; automated tests simulate those writes. Changes made outside the app can affect restoration; the restore review warns if Windows is currently using another plan. Update checks use the newest compatible published release from the most recent 20 releases; bump the app's numeric project version when publishing an update. A release with the same numeric version is not offered again.

## References

- [Microsoft power-management APIs](https://learn.microsoft.com/en-us/windows/win32/power/power-management-functions)
- [Microsoft Windows Settings URIs](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings)
- [Microsoft .NET single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- [Official FiveM installation guide](https://docs.fivem.net/docs/client-manual/installing-fivem/)

Independent community tool. Not affiliated with Cfx.re, Rockstar Games, or Microsoft. MIT licensed.
