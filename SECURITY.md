# Security, privacy, and recovery

## What is changed

After a confirmation dialog, `WindowsPowerSystem.SetActive` calls the documented `PowerSetActiveScheme` API. The original GUID is saved before the call. No elevation is requested. Windows policy can deny a change; the error is shown without attempting a workaround.

Only one normal instance may run per Windows session. UI writes are serialized on the UI thread, with only one outstanding backup allowed. Backups use a temporary file, a flush to disk, and atomic replacement. Supported journal states are `Prepared`, `Applied`, and `Restored`. A failed write or verification leaves a pending backup available. Corrupt or unknown-format backups block new changes and are not silently reset.

The backup is a convenience for the same user account, not an authenticated security boundary. Do not copy an untrusted backup into the app's data folder. The app validates the schema and requires the original GUID to be present in Windows before restoring it. It does not execute content from the file.

## Manual power-plan recovery

If the UI cannot restore, keep a copy of `%LOCALAPPDATA%\UhFiveMOptimizer\power-backup.json`. Its `OriginalId` is the original plan's GUID. Open Windows Power Options and select the original plan by name, or inspect the GUID with the built-in `powercfg /list` command and use `powercfg /setactive <original-guid>` when appropriate for your PC. Do not delete the backup until recovery is complete.

If a plan has been removed by Windows, the app cannot recreate it. Group policy restrictions are respected. Windows Settings changes are outside the power-plan journal.

## Files and diagnostics

### Graphics and optional visual files

Low-end settings edit only an existing `gta5_settings.xml` with the recognized version-27 Settings/graphics structure. The graphics actions require an explicit GTA V Legacy selection; unsupported formats are refused. Enhanced users should use in-game settings; the app does not bypass pure mode or other server restrictions. XML parsing forbids DTDs/external entities. Unsupported fields are preserved; supported missing fields are not invented. Resolution reduction is opt-in. No read-only flag is imposed on the game's settings.

The optional legacy importer performs no download or code execution. It accepts only bounded ZIP entries under the exact `citizen/common/data/visualsettings.dat` and `citizen/common/data/timecycle/*.xml` paths. Traversal, duplicate targets, other file types and excessive expansion are rejected before writes. Original-pack compatibility is unverified; import is experimental and requires the player's review and server permission. No third-party pack is redistributed by this repository.

Separate graphics/visual journals record original file bytes and hashes. Every batch is prepared and flushed before any target writes. Targets are checked for linked ancestors, written by sibling-temp atomic replacement, and verified. A crash or interrupted batch leaves a Prepared journal for recovery. Pending or corrupt backups block another apply. File restoration removes only files introduced by that batch and restores byte-exact originals. Newer user/game edits require explicit review and are saved as `before-restore-*.json` before replacement. Keep those records local: they include personal file paths and full contents of supported settings/visual files. To recover manually, `Original` values in `files-backup.json` are base64-encoded bytes and `OriginalHash` is SHA-256; a null Original means the app introduced that file. Preserve the journal before manual changes.

FiveM/GTA V detection blocks mutation and is rechecked during batches; the app does not close those processes. Another process starting or changing files concurrently may interrupt a batch, so leave games closed until completion. Backups are same-user recovery data, not a security boundary against malicious software running under that account.

### Temporary-file deletion

Cleanup requires a preview and an explicit permanent-deletion confirmation. It is not automatic on startup, launch or shutdown, and has no undo. Roots are selected by the app from standard user/Windows Temp and optional Prefetch paths. A redirected `%TEMP%` must be a local folder named Temp. Roots and ancestors with reparse points are excluded. Scans are bounded to 20 seconds/50,000 entries, keep root directories, skip protected/recent files and do not descend into Prefetch subfolders. Prefetch is off by default because those files assist launch performance.

Every candidate records its Windows volume/file ID, length, creation and modification time through a non-following metadata handle. Before deletion the Windows implementation opens that file with exclusive DELETE/READ_ATTRIBUTES access and OPEN_REPARSE_POINT, rejects links/directories/protected attributes, validates the file ID, both timestamps, length, link count and final handle path, then marks that verified handle for deletion. Locked/inaccessible/changed candidates are skipped. The preview expires after ten minutes; changing selections invalidates it. Cancellation or a detected game launch stops remaining work and reports the number already removed. No elevation, permission changes, recursive folder removal, FiveM cache deletion, or forced process termination is used.

Cache inspection is bounded by time and file count, skips reparse-point entries, and never modifies cache contents. Hardware and resource reads do not intentionally write system settings. Diagnostic exports include hardware, OS, disk, installation path, and whole-PC CPU/RAM samples. They omit account tokens, server credentials, browsing history, and memory contents. Installation paths can contain your Windows username.

## Release verification

### In-app updates

Checks occur only when requested on the Updates page. The feed is fixed to `OnniUh/uh-fivem-optimizer`; update URLs must use that repository's release-download path. HTTPS redirects are restricted to GitHub and its known release-asset hosts. No GitHub token, account information, hardware scan, or diagnostic report is sent.

The checker validates build metadata against GitHub's asset digest, requires the recorded source commit to match the release target, and compares numeric app versions. It checks size bounds, checksum metadata, and the EXE's SHA-256 before installation. Checks do not download the EXE. Completed verified files are cached by hash and rechecked before reuse; canceled or invalid partial files are removed.

Installation is user-confirmed and available in standalone release EXEs. A verified copy launches as a temporary helper. The helper rechecks its own bytes, the requested parent process and start time, target path, and staged copy. It stages in the installation folder before the parent closes, waits for that exact parent to exit, and uses `File.Replace` with a previous-EXE backup. Failed handoff is canceled so it cannot apply later. Failed process launch rolls back; failure after a successful process start requires manual use of the retained `.previous` file.

The updater is not elevated. Protected installation folders can prevent replacement; no UAC or security-policy workaround is attempted. The local cache and handoff request are not security boundaries against another process running as the same Windows user. Verified download caches and the previous EXE use disk space and can be removed manually after a successful update.

Releases remain unsigned. Hashes check integrity against the trusted GitHub feed; they do not authenticate a publisher independently of that feed. Compromise of the GitHub account or workflow could affect updates. These checks do not replace publisher code signing or an independent audit.

### Build and checksum provenance

Each preview tag points to one source commit. The Windows build workflow runs tests, publishes Microsoft's self-contained runtime with the app, records the commit and SDK, and attaches SHA-256 checksums. The app is currently unsigned and has not been independently audited. Checksums confirm identity against a trusted copy; they do not establish safety if both the binary and checksum are replaced.

Do not disable antivirus or SmartScreen to satisfy this app. Review source or build it yourself if a release is not trusted. A publisher signing certificate and a broader pilot-device validation pass are future release work.

## Reporting a concern

For non-sensitive bugs, open a repository issue. Do not post tokens, full diagnostic exports, personal installation paths, or sensitive exploit details in a public issue. A private vulnerability-reporting channel has not yet been configured.
