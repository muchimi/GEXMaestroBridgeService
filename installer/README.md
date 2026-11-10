# GEX Maestro Bridge installer

The Release build can optionally create a Windows x64 setup program and publish it to `muchimi/GEXMaestroBridgeService`. Both options are off by default.

## Build options

From the repository root:

```powershell
# Build the bridge only.
dotnet build -c Release

# Build the bridge and its setup program.
dotnet build -c Release -p:BuildInstaller=true

# Build the setup program and publish a GitHub release.
dotnet build -c Release -p:PublishGitHubRelease=true
```

Use Windows, .NET 10 SDK, Inno Setup 6 or 7, and the complete official HIDMaestro 2.x `lib\HIDMaestro.Core.dll` containing its native driver payloads. Set `ISCC_EXE` or `-p:InnoSetupCompiler="C:\path\ISCC.exe"` if the compiler is not on PATH or in a standard installation directory. `-p:HidMaestroDll="C:\path\HIDMaestro.Core.dll"` overrides the SDK location. Debug and mock builds cannot be packaged.

Ordinary build copies go to `artifacts\build\<configuration>\<platform>`. Packaging performs a separate self-contained `win-x64` publish into a fresh directory under `artifacts\installer-payload`, stages the license files, verifies the bridge's `--version`, and produces:

```text
artifacts\installer\GEXMaestroBridgeSetup-0.1.1-x64.exe
```

Installer output is separate from its payload, so a previous installer cannot enter the next package. Nested publish disables the packaging/publishing targets to prevent recursion. No build step clears the existing `dist` directory.

The setup can also be built directly:

```powershell
.\installer\build-installer.ps1
.\installer\build-installer.ps1 -PublishRelease
```

## Windows Command Prompt scripts

Every PowerShell script under `installer` and `scripts` has a matching `.cmd` launcher. The launchers run the same PowerShell implementation, forward all arguments, and return its exit code. They use Windows PowerShell 5.1 included with Windows. Run them from Command Prompt (`cmd.exe`); use an elevated terminal for installation, service, firewall, and upgrade operations.

From the repository root:

```cmd
rem Build the setup program.
installer\build-installer.cmd

rem Build the setup program and publish a GitHub release.
installer\build-installer.cmd -PublishRelease

rem Publish the existing setup for the version in the bridge project.
installer\publish-release.cmd

rem Publish a specific version and setup file.
installer\publish-release.cmd -Version 0.1.1 -Installer "artifacts\installer\GEXMaestroBridgeSetup-0.1.1-x64.exe"

rem Run offline installer checks.
installer\test-release.cmd
installer\test-configure.cmd
```

| Command Prompt launcher | Purpose |
| --- | --- |
| `installer\build-installer.cmd` | Build the installer; accepts `-Version`, `-BridgeProject`, `-HidMaestroDll`, `-Iscc`, and `-PublishRelease`. |
| `installer\publish-release.cmd` | Publish or replace the versioned installer; accepts `-Version` and `-Installer`. |
| `installer\configure.cmd` | Configure a deployed bridge; accepts the same required `-InstallDir` and network/service options as `configure.ps1`. |
| `installer\upgrade.cmd` | Run the upgrade helper with `-InstallDir`, `-Version`, and `-Action prepare`, `rollback`, or `complete`. |
| `installer\test-release.cmd` | Run mocked release checks without GitHub writes. |
| `installer\test-configure.cmd` | Run mocked service/firewall configuration checks. |
| `scripts\install-service.cmd` | Install a published bridge folder with `-PublishDirectory`. Requires Administrator privileges. |
| `scripts\uninstall-service.cmd` | Remove the bridge service registration. Requires Administrator privileges. |
| `scripts\validate-source.cmd` | Run the source validation workflow; accepts `-IncludeRealBuild`. |

Quote paths containing spaces. Each launcher locates its companion `.ps1` beside itself, so it can also be invoked with an absolute path from another directory. When invoking a `.cmd` launcher from another batch file, use `call` to return to the caller afterward.

Setup also installs `configure.cmd` and `upgrade.cmd` beside their PowerShell implementations under `C:\GremlinEx\GEXMaestroBridge\installer`. For example, to configure an installed bridge for local service operation:

```cmd
"C:\GremlinEx\GEXMaestroBridge\installer\configure.cmd" -InstallDir "C:\GremlinEx\GEXMaestroBridge" -Mode service -ListenPort 8010 -SendPort 8011 -Remote false -FirewallScope none
```

## Versioned GitHub releases

`Version` in `src\MaestroOscService\GEXMaestroBridgeService.csproj` is the source of the assembly version, reported application version, installer version/name, and release tag. It must be `major.minor.patch`. A build with version `0.1.1` publishes tag **`0.1.1`** and asset **`GEXMaestroBridgeSetup-0.1.1-x64.exe`**, matching GremlinEx's optional dependency downloader. You can override the version consistently with `-p:Version=0.1.2`.

Publishing authenticates through `GH_TOKEN`, `GITHUB_TOKEN`, or Git Credential Manager. The credential needs Contents read/write access to `muchimi/GEXMaestroBridgeService`. Commit and push the release source first: the release/tag targets the local HEAD commit, which must exist on GitHub. The build does not commit or push source changes.

The publisher creates a draft, uploads and checks the setup asset, then publishes a stable release. GitHub chooses Latest using creation date and semantic version (`make_latest=legacy`), allowing GremlinEx's `/releases/latest` lookup to find it. Upload failure leaves a newly created release as a draft. An identical uploaded asset is reused. A different or incomplete setup for the same version is replaced automatically: the publisher uploads and verifies a temporary replacement, removes the old matching installer assets, and renames the new upload to the expected filename. The existing release/tag and unrelated assets are retained. If the replacement upload fails verification, the prior installer is retained. GitHub immutable releases cannot be changed.

To publish an already built setup:

```powershell
.\installer\publish-release.ps1 -Version 0.1.1 -Installer .\artifacts\installer\GEXMaestroBridgeSetup-0.1.1-x64.exe
```

## Installation and validation

See the [bridge command-line guide](../COMMAND-LINE.md) for running the installed executable, setting OSC ports and addresses, managing the service, and using mock mode.

Setup defaults to `C:\GremlinEx\GEXMaestroBridge` and offers service/console mode, OSC ports, local/remote GremlinEx, and firewall scope. Service mode runs as LocalSystem; driver and service operations require elevation. Existing `appsettings.json` is preserved. Default GEX 1-4 device creation remains unimplemented. Windows service, driver, controller and upgrade behavior still require manual validation before release; compilation alone does not validate those operations.

All files from `licenses\GPL-3.0.txt`, `HIDMaestro-MIT.txt`, `THIRD-PARTY-NOTICES.txt`, and `README.md` are included under the installed `licenses` folder. The bridge GPL license is also displayed by setup.

Run the mocked release workflow checks without network access or GitHub writes:

```powershell
.\installer\test-release.ps1
.\installer\test-configure.ps1
```
