# Windows PATH Repair — 2026-07-30

## Summary

Repaired the current user's Windows User and System PATH values after diagnosing repeated PATH truncation/rewrite behavior. The unsafe draft script was replaced with a reusable, preservation-first PowerShell audit/repair/verify script.

## Diagnosis

- Initial persisted User PATH: `REG_EXPAND_SZ`, 1 entry, 50 characters.
- Initial persisted System PATH: `REG_EXPAND_SZ`, 2 entries, 97 characters.
- The initial System PATH incorrectly contained `%USERPROFILE%\AppData\Local\Microsoft\WindowsApps`, a per-user variable in machine scope.
- Current process PATH was stale and only contained ToCodex/WindowsApps/VS Code Insiders shim entries; existing shells were expected to remain stale.
- User environment key ACL allowed the logged-in user `FullControl`; a harmless temporary-value write/read/delete probe passed.
- System environment key had standard inherited SYSTEM/Administrators full-control ACLs; the non-elevated process correctly received access denied on the harmless write probe.
- No matching scheduled task, Run entry, or visible process command line was identified as a PATH writer. However, the User PATH changed between read-only audits without this script writing it, including a type change to `REG_SZ`; this confirms an external GUI/process rewrite or race as the reason entries disappeared.
- No policy environment key was found in the checked policy locations.

## Repair behavior

The revised [`fix-path-environment.ps1`](../fix-path-environment.ps1) now:

- Defaults to read-only `Audit` mode.
- Uses direct .NET registry APIs and never uses `setx`.
- Exports both environment registry keys before every repair.
- Preserves valid existing directory entries, removes malformed/quoted/self-referential entries, and de-duplicates case-insensitively.
- Keeps machine-wide paths in System PATH and per-user paths in User PATH; moves the malformed `%USERPROFILE%` machine entry to User scope.
- Writes both values as `REG_EXPAND_SZ`.
- Restores only verified Windows/tool directories, including Windows System32/Windows/Wbem/WindowsPowerShell/OpenSSH, Python 3.14, Node.js, Git, PowerShell 7, dotnet, Go, Docker, Java, Google Cloud CLI, Azure CLI, VS Code, npm global bin, Hermes, Cargo, .NET tools, WindowsApps, and verified Hermes utilities.
- Checks conservative PATH length limits, broadcasts `WM_SETTINGCHANGE`, verifies persistence, and runs fresh child-shell command checks.
- Attempts rollback of both PATH values if either write fails and creates a restore script with each backup.

## Backups

- Initial diagnostic backup: `C:\Users\Gabi.WIN-CD45QMUUPFF\PathBackups\path-repair-20260730-045631`
- Repair backup/report after first successful repair: `C:\Users\Gabi.WIN-CD45QMUUPFF\PathBackups\path-repair-20260730-050718`
- Final repair backup/report after adding verified OpenSSH: `C:\Users\Gabi.WIN-CD45QMUUPFF\PathBackups\path-repair-20260730-050954`

Each repair backup contains HKCU/HKLM `.reg` exports, raw before/after PATH text, metadata, a restore script, and the final JSON report. Backup files may contain unrelated environment values; they should remain private.

## Final persisted state

- User PATH: `REG_EXPAND_SZ`, 12 entries, 515 characters.
- System PATH: `REG_EXPAND_SZ`, 17 entries, 544 characters.
- Both values were independently re-read from the registry after the final elevated repair.
- `WM_SETTINGCHANGE` was broadcast by the repair script.

## Fresh-shell verification

The final `Verify` run and an explicit fresh `cmd.exe` trace both passed:

- `where.exe`: resolved from `C:\Windows\System32\where.exe`.
- `python`: resolved; Python 3.14.6 from `C:\Python314`.
- `py`: resolved; Python 3.14.4 launcher.
- `node`: resolved; v25.0.0.
- `npm`: resolved; 11.6.2.
- `npx`: resolved; 11.6.2.
- `code`: resolved; VS Code 1.126.0.
- `git`: resolved; 2.55.0.windows.3.
- `dotnet`: resolved; 11.0.100-preview.5.26302.115.
- `pwsh`: resolved; PowerShell 7.6.4.
- `hermes`: resolved; Hermes Agent v0.19.0 (2026.7.20).
- Optional discovered tools also resolved: Go 1.26.5, Docker 29.0.1, and Cargo 1.95.0.
- Fresh Windows PowerShell resolution returned the expected sources for Python, Node/npm, VS Code, Git, dotnet, PowerShell 7, and Hermes.

## Remaining risks

- Existing terminals and applications retain their inherited stale PATH until restarted; new shells are correct.
- The external PATH rewrite source was not identified in the checked scheduled tasks, startup Run keys, policies, or visible process command lines. If the GUI/process continues rewriting PATH, monitor the registry after restarting the responsible application and use the backup restore scripts if needed.
- User PATH intentionally includes stable user-scoped tool directories discovered on this workstation; future uninstallations may leave stale entries, which can be removed later with `-Mode Audit` followed by a reviewed repair.
