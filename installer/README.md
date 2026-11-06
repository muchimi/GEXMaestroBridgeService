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
artifacts\installer\GEXMaestroBridgeSetup-0.1.0-x64.exe
```

Installer output is separate from its payload, so a previous installer cannot enter the next package. Nested publish disables the packaging/publishing targets to prevent recursion. No build step clears the existing `dist` directory.

The setup can also be built directly:

```powershell
.\installer\build-installer.ps1
.\installer\build-installer.ps1 -PublishRelease
```

## Versioned GitHub releases

`Version` in `src\MaestroOscService\GEXMaestroBridgeService.csproj` is the source of the assembly version, reported application version, installer version/name, and release tag. It must be `major.minor.patch`. A build with version `0.1.0` publishes tag **`0.1.0`** and asset **`GEXMaestroBridgeSetup-0.1.0-x64.exe`**, matching GremlinEx's optional dependency downloader. You can override the version consistently with `-p:Version=0.1.1`.

Publishing authenticates through `GH_TOKEN`, `GITHUB_TOKEN`, or Git Credential Manager. The credential needs Contents read/write access to `muchimi/GEXMaestroBridgeService`. Commit and push the release source first: the release/tag targets the local HEAD commit, which must exist on GitHub. The build does not commit or push source changes.

The publisher creates a draft, uploads and checks the setup asset, then publishes a stable release. GitHub chooses Latest using creation date and semantic version (`make_latest=legacy`), allowing GremlinEx's `/releases/latest` lookup to find it. Upload failure leaves a newly created release as a draft. An identical uploaded asset can be reused; a different setup for the same version is rejected instead of replacing released binaries. Increment `Version` when releasing changed binaries.

To publish an already built setup:

```powershell
.\installer\publish-release.ps1 -Version 0.1.0 -Installer .\artifacts\installer\GEXMaestroBridgeSetup-0.1.0-x64.exe
```

## Installation and validation

Setup offers service/console mode, OSC ports, local/remote GremlinEx, and firewall scope. Service mode runs as LocalSystem; driver and service operations require elevation. Existing `appsettings.json` is preserved. Default GEX 1-4 device creation remains unimplemented. Windows service, driver, controller and upgrade behavior still require manual validation before release; compilation alone does not validate those operations.

All files from `licenses\GPL-3.0.txt`, `HIDMaestro-MIT.txt`, `THIRD-PARTY-NOTICES.txt`, and `README.md` are included under the installed `licenses` folder. The bridge GPL license is also displayed by setup.

Run the mocked release workflow checks without network access or GitHub writes:

```powershell
.\installer\test-release.ps1
.\installer\test-configure.ps1
```
