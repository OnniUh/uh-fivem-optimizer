# Security, privacy, and recovery

## What is changed

After a confirmation dialog, `WindowsPowerSystem.SetActive` calls the documented `PowerSetActiveScheme` API. The original GUID is saved before the call. No elevation is requested. Windows policy can deny a change; the error is shown without attempting a workaround.

Only one normal instance may run per Windows session. UI writes are serialized on the UI thread, with only one outstanding backup allowed. Backups use a temporary file, a flush to disk, and atomic replacement. Supported journal states are `Prepared`, `Applied`, and `Restored`. A failed write or verification leaves a pending backup available. Corrupt or unknown-format backups block new changes and are not silently reset.

The backup is a convenience for the same user account, not an authenticated security boundary. Do not copy an untrusted backup into the app's data folder. The app validates the schema and requires the original GUID to be present in Windows before restoring it. It does not execute content from the file.

## Manual power-plan recovery

If the UI cannot restore, keep a copy of `%LOCALAPPDATA%\UhFiveMOptimizer\power-backup.json`. Its `OriginalId` is the original plan's GUID. Open Windows Power Options and select the original plan by name, or inspect the GUID with the built-in `powercfg /list` command and use `powercfg /setactive <original-guid>` when appropriate for your PC. Do not delete the backup until recovery is complete.

If a plan has been removed by Windows, the app cannot recreate it. Group policy restrictions are respected. Windows Settings changes are outside the power-plan journal.

## Files and diagnostics

Cache inspection is bounded by time and file count, skips reparse-point entries, and never modifies cache contents. Hardware and resource reads do not intentionally write system settings. Diagnostic exports include hardware, OS, disk, installation path, and whole-PC CPU/RAM samples. They omit account tokens, server credentials, browsing history, and memory contents. Installation paths can contain your Windows username.

## Release verification

Each preview tag points to one source commit. The Windows build workflow runs tests, publishes Microsoft's self-contained runtime with the app, records the commit and SDK, and attaches SHA-256 checksums. The app is currently unsigned and has not been independently audited. Checksums confirm identity against a trusted copy; they do not establish safety if both the binary and checksum are replaced.

Do not disable antivirus or SmartScreen to satisfy this app. Review source or build it yourself if a release is not trusted. A publisher signing certificate and a broader pilot-device validation pass are future release work.

## Reporting a concern

For non-sensitive bugs, open a repository issue. Do not post tokens, full diagnostic exports, personal installation paths, or sensitive exploit details in a public issue. A private vulnerability-reporting channel has not yet been configured.
