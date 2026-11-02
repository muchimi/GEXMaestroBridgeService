# GEX Maestro Bridge installer (development)

Inno Setup 6 source for an offline Windows x64 installer. This is an **unvalidated prototype**, not a tested release.

## Build

On Windows with .NET 10 SDK, Inno Setup 6 and PowerShell:

```powershell
.\installer\build-installer.ps1 -BridgeProject .\src\MaestroOscService\MaestroOscService.csproj -Version 0.1.0 -HidZipSha256 '<independently verified SHA-256>'
```

Build downloads HIDMaestro v2.1.0, verifies the ZIP hash, publishes a self-contained .NET application, then packages it. Check third-party license files before redistribution.

## Current behavior and limitations

- Installer prompts for service or console mode, OSC ports, local or remote GremlinEx, and firewall scope.
- Setup requests administrator privileges. HIDMaestro driver operations and service installation require elevation.
- Service runs as LocalSystem. Console mode does not register a service.
- Localhost mode binds to 127.0.0.1; remote mode listens on 0.0.0.0 and can create an inbound firewall rule.
- **Default GEX 1-4 device creation is NOT implemented** despite the UI checkbox. Do not advertise it as working.
- **Upgrade/rollback logic is NOT complete**. Existing service files may be locked while upgrading.
- Installer and build have NOT been compiled or tested on Windows.
- Verify executable name, dependency layout, driver behavior, appsettings merging and service lifecycle before release.
- A GitHub Actions release workflow has not yet been added.

Do not publish this as production-ready until these limitations are resolved and Windows testing passes.

## License packaging

The build script validates and copies all four files from the repository's `licenses/` directory into `payload/licenses/`. The Inno Setup `[Files]` recursive payload entry installs them under `{app}\\licenses`. The wizard also displays the bridge GPL-3.0 license. HIDMaestro's MIT license and third-party notices remain available in the installed licenses folder. Do not delete these files from distribution packages.
