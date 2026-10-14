# Uh FiveM Optimizer

**Tune your setup. See every change.** A small, open-source Windows desktop app by Uh for FiveM players.

> Version 0.1.0 is an early preview. It provides diagnostics and reversible Windows power-plan selection. It does not measure game FPS or guarantee a performance increase. Test it on a few PCs before recommending it to all players.

## Download

Get a Windows x64 preview from [GitHub Releases](https://github.com/OnniUh/uh-fivem-optimizer/releases). Each successful build on `main` publishes a separate prerelease tied to its source commit. Download `UhFiveMOptimizer.exe`, `SHA256SUMS.txt`, and `build-info.json` from the **same release**.

Run the EXE as a normal user. No installer or separate .NET installation is needed for the published self-contained EXE. The app is currently **unsigned**: it does not have a publisher code-signing certificate. It does not ask you to disable Windows security software.

Requires a currently supported x64 Windows 10/11 environment compatible with .NET 10. ARM64 and Windows 7/8 are not targeted. Keep Windows and your graphics driver updated through their official channels.

## Included in v0.1.0

- CPU, GPU, physical-memory, operating-system, and free-storage scan.
- Live whole-PC CPU/RAM charts, sampled every two seconds, with up to 60 samples.
- FiveM detection at its standard location, plus a manual folder picker.
- Bounded, read-only cache size inspection. No game/cache files are deleted.
- Selection of **existing** Windows power plans with an explicit before/after review.
- An atomic, on-disk backup saved **before** changing the power plan, followed by verification.
- Restoration after closing/reopening the app or restarting Windows.
- Windows Settings shortcuts for Game Mode, graphics preferences, background captures, and startup apps.
- User-initiated JSON diagnostic export.

Only the active Windows power plan is changed automatically after confirmation. The app does not alter antivirus, Windows services, anti-cheat, game files, registry settings, process priorities, network settings, or hidden power plans. Windows Settings shortcuts open the corresponding page; settings changed there are not covered by the app's backup.

## How to use

1. Open the app and review **Overview**. Scan again or choose **Locate FiveM** if necessary.
2. Open **Tune your setup**, choose a plan, and click **Review change**. Read the exact change and confirm if you want it.
3. Closing the app leaves the chosen plan active. Use **Restore** to return to your saved original plan.
4. To try a different plan, restore the outstanding backup first.

Higher-performance power plans can increase heat, fan noise, and battery use. Some PCs expose only one plan or use Windows Power mode instead. The app respects the plans that Windows exposes. A server with expensive scripts or assets can remain slow regardless of PC settings.

## Privacy and trust

The app has no network client, analytics, account system, advertising, auto-updater, or background service. Reads and backups remain local. Settings links open Windows Settings. A diagnostic report is saved only to a location you choose and can contain your Windows username in installation paths; review it before sharing.

App state is under `%LOCALAPPDATA%\UhFiveMOptimizer`:

| File | Purpose |
| --- | --- |
| `power-backup.json` | Original and target plan GUIDs, names, timestamps, and recovery status |
| `fivem-path.txt` | A manually selected FiveM installation folder |

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

The smoke test writes four rendered page PNGs and a scan result to the chosen folder, then exits. Those results contain machine details and should be reviewed before sharing. Recovery tests use a fake power provider: they never modify Windows settings.

## Project layout

```text
src/UhOptimizer.App/       WPF desktop UI and Windows API integration
src/UhOptimizer.Core/      Recovery journal and bounded FiveM path/cache inspection
tests/UhOptimizer.Tests/   Dependency-free behavioral test runner
.github/workflows/        Windows build, test, artifact, and preview-release workflow
docs/                     Release notes and manual validation checklist
```

## Current limits

This first version does not include in-game frame-time capture, graphics-preset editing, cache cleaning, driver installation, overlays, or automatic updates. Real power-plan apply/restore should be validated on a pilot PC; automated tests simulate those writes. Changes made outside the app can affect restoration; the restore review warns if Windows is currently using another plan.

## References

- [Microsoft power-management APIs](https://learn.microsoft.com/en-us/windows/win32/power/power-management-functions)
- [Microsoft Windows Settings URIs](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings)
- [Microsoft .NET single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- [Official FiveM installation guide](https://docs.fivem.net/docs/client-manual/installing-fivem/)

Independent community tool. Not affiliated with Cfx.re, Rockstar Games, or Microsoft. MIT licensed.
