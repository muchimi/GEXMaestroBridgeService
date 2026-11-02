# In-place upgrade workflow

The installer compares the installed bridge executable's `--version` against the new installer version. Same-version installations and downgrades are rejected when the installed executable exists.

Before replacement, the upgrade script stops the registered `GEXMaestroBridgeService` Windows service and refuses to proceed while the console executable remains running. It backs up the current installation under `%ProgramData%\GEXMaestroBridge\upgrade`.

The installer preserves existing `appsettings.json` on upgrade. The wizard defaults to the existing service mode if the service is registered. Service configuration and firewall changes are skipped on upgrade; fresh installs run `configure.ps1`.

On installation failure, the rollback script restores the backed-up installation and restarts the service if it had been running. This is a best-effort file-level rollback, **not a fully transactional rollback**: Inno Setup may have modified its uninstall database and external driver state. Test Windows service and console upgrades before publishing.

Existing installations from the earlier prototype used inconsistent executable and service names; these may need manual migration. Device definitions stored outside the installation folder are not backed up by this workflow.
