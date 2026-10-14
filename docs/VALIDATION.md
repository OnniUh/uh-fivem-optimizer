# Pilot-device validation

Automated recovery tests simulate system writes; the UI smoke test performs read-only hardware scans. Before recommending this preview to all players, validate on a small set of supported Windows x64 PCs.

- Start as a normal user; scan and close without applying anything. Confirm the active power plan remains unchanged.
- Check CPU, RAM, GPU, and free disk against Windows. Select a custom FiveM location and restart the app to confirm it persists.
- Choose a different available plan. Cancel the review and confirm nothing changed.
- Apply after confirming. Check Windows Power Options and the original GUID saved in the backup.
- Close and reopen the app, then restore. Confirm Windows uses the exact original plan.
- Restart Windows while a backup is pending, then restore.
- Try a PC exposing only one plan and one with policy restrictions. Errors should be readable and recovery records preserved.
- With a pending change, manually pick another plan in Windows. The Restore review should warn before replacing it.
- Confirm reports are exported only after a save dialog, and review personal paths before sharing.
- Review all seven pages at minimum window size and at 125%/150% display scaling; use keyboard navigation and scroll the longer pages.
- Compare actual game frame times with an external trusted measurement tool on the same server and scene. Record hardware, settings, and server workload. Do not infer FPS improvements from the CPU/RAM graphs.

The app does not change system settings on startup, scan, page navigation, or close. Do not use Windows security bypasses as part of testing.

## Graphics and cleanup pilot (0.3.0)

Automated tests use disposable settings/ZIP/temp fixtures. They establish file-handling behavior, not current FiveM compatibility or FPS gains. The original 2017 visual pack was not available for direct validation; its importer is tested with representative archives and rejects unsupported layouts.

- Record GTA V edition, FiveM build, CPU/GPU/RAM, resolution, server, player load, average FPS and frame-time lows before changing anything. Use the same measurement tool, route, weather and scene for repeat runs. Do not use whole-PC CPU/RAM charts as FPS measurements.
- On GTA V Legacy, launch once then close both GTA V and FiveM. Check that the detected Roaming/CitizenFX/gta5_settings.xml is the file used by that installation. Unsupported schema versions must be refused. On Enhanced, use in-game graphics settings; do not import the Legacy visual pack.
- Preview the low-end preset, cancel, and compare the file hash. It must be unchanged. Apply after review; confirm only listed settings differ, adapter/monitor/refresh/VSync remain intact and resolution only reduces when explicitly selected.
- Launch FiveM. Verify settings, visuals, load/join behavior and performance on the actual server. Re-test after game/build updates. Close the game, reopen the optimizer, restore, and compare the complete file hash with the original.
- Edit one supported setting manually after applying. Restore must warn about later edits and preserve those edits separately before restoring the original. Keep backups until testing is complete.
- If testing the author's old pack on an allowed Legacy server, use a separate pilot installation. Preview its actual ZIP first. Unsupported archive layouts must fail without changing files; do not work around rejection by copying the entire citizen folder. Test loading, weather, night visibility, texture streaming and restoration of both overwritten and newly introduced files.
- Close apps before temp cleanup. Preview and inspect the listed paths. Cancel review and verify files remain. Test deletion with disposable old files, a locked file, and a newly modified file; only eligible unchanged files should disappear. Temp directories remain. Check counts after cancellation.
- Leave Prefetch unchecked for normal sessions. If deliberately testing it, only old .pf files directly inside Prefetch are candidates; ReadyBoot/Layout.ini remain. Access-denied files are skipped without elevation. Compare launch times, not just disk space.
- Press Launch FiveM: it should start the selected launcher and minimize the optimizer without changing power plans or deleting files automatically.

The Windows CI run requires junction/hard-link fixtures and runs all behavioral checks plus the actual EXE update/rollback probe. Restricted local sandboxes explicitly skip link-fixture creation; those skips are not counted as passes.
