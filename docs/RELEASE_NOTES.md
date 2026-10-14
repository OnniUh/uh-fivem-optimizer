# Uh FiveM Optimizer 0.3.1 — graphics and cleanup preview

Adds reviewed optimization choices to the native UI, with file recovery and permanent-cleanup previews.

- **Low-end graphics for GTA V Legacy**: preview supported setting changes in the recognized gta5_settings.xml format; reduce graphics quality, draw distance and MSAA. Optional resolution reduction to at most 1280×720. GPU/monitor/refresh/VSync and unknown fields stay intact.
- **Graphics Recovery**: original bytes are saved before target writes; interrupted batches remain recoverable. Restore detects later edits and saves them separately after explicit review.
- **Temporary-file cleanup**: preview files created and modified more than seven days ago in user Temp, supported redirected %TEMP%, and Windows Temp. Permanent deletion requires a separate review. Changed, active, protected and linked files are skipped; directories remain.
- **Prefetch**: advanced unchecked option for old .pf files only, marked not recommended. It can slow subsequent app launches and is not an FPS optimization.
- **Linked community visual pack**: separate experimental importer for supported visualsettings.dat/timecycle ZIP entries, with original-file backup and introduced-file removal on restore. Download the author's pack from its original Cfx.re thread. The pack is not bundled, and its actual archive/current compatibility has not been validated.
- **Edition checks**: graphic options target Legacy. Enhanced currently blocks graphics mods; use its in-game settings. No pure-mode bypass is included.
- **Launch FiveM**: starts the selected launcher and minimizes the optimizer without automatically deleting files, changing power plans or closing apps.
- Preserves saved themes, native motion controls, power-plan recovery, diagnostics and verified in-app updates.
- Fixes update checks when GitHub returns older releases first: scans the bounded feed and selects the highest compatible numeric version, with mixed-order regression tests and a real-feed probe simulating an older client.

Windows CI runs behavioral file/recovery/cleanup/update tests, requires junction and hard-link safety checks, and exercises the actual published EXE's update helper and rollback in a disposable folder. The UI smoke test renders all seven pages at two sizes and round-trips disposable graphics settings.

**Performance is unmeasured.** These options trade visual quality for potential rendering headroom and reclaim storage. They cannot promise a jump from 30–40 FPS to 80–90 FPS. Server resources, CPU limits, hardware and scenes matter. Validate on a few pilot devices and your actual server before broad distribution.

**Install v0.3.1 manually once from this release.** The v0.2.0/v0.3.0 updater can miss new releases on the current feed because it assumes newest-first ordering. The corrected updater in this version supports future newer releases through **Updates → Check for updates → Download & restart**. This Windows x64 EXE is self-contained and unsigned. Read README.md, SECURITY.md and docs/VALIDATION.md before distributing.
