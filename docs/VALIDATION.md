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
- Review all four pages at minimum window size and at 125%/150% display scaling; use keyboard navigation and scroll the longer pages.
- Compare actual game frame times with an external trusted measurement tool on the same server and scene. Record hardware, settings, and server workload. Do not infer FPS improvements from the CPU/RAM graphs.

The app does not change system settings on startup, scan, page navigation, or close. Do not use Windows security bypasses as part of testing.
