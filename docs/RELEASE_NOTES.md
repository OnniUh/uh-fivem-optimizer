# Uh FiveM Optimizer 0.2.0 — UI and updates preview

New native styling, saved colors, motion controls, and an in-app updater by Uh.

- Glossy pill buttons, pointer highlights, and subtle native vector artwork inspired by the supplied Sylva reference.
- Appearance page: six color presets, custom hex colors, live previews, readable text, and saved preferences.
- Reduced motion, Windows animation preference support, and motion pause while FiveM runs or the app is inactive/minimized.
- Updates page: user-initiated checks, progress/cancellation, verified downloads, in-place EXE replacement, and restart.
- No repeat EXE download for an installed version; verified cached files can be reused for install retries.
- Previous EXE retained for recovery; appearance and power-plan backups are preserved.
- 41 behavioral tests, UI/motion smoke checks, live GitHub metadata validation, and a real EXE installer probe in a disposable folder.

Install this version once if you use 0.1.0; future newer releases can install through Updates. Each new version downloads a complete EXE. Incremental patching and unattended updates are not included.

Existing optimizer features:

- Hardware scan, live whole-PC CPU/RAM graphs, and FiveM location selection.
- Read-only cache sizing and optional local diagnostic export.
- Review-before-apply selection of existing Windows power plans.
- Durable backup before changes, verification, and a Restore page.
- Shortcuts to Windows gaming, graphics, recording, and startup settings.
- Public C# source, MIT license, behavioral tests, and inspectable build workflow.

The EXE is unsigned. This is a pilot preview, not a measured FPS booster. No FPS gain is guaranteed. Real-device validation across several PCs is still needed. Read the README and SECURITY.md before distributing broadly.

Download the EXE and checksums from this release together. `build-info.json` records the exact source commit. Windows x64 only; self-contained builds include the .NET runtime. No setup program is needed.
