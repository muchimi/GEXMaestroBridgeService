# GEX Maestro Bridge command-line guide 0.1.1

The bridge executable is `GEXMaestroBridgeService.exe`. It runs as a console application when launched from a terminal and as a Windows service when launched by Windows Service Control Manager. The registered service name is `GEXMaestroBridgeService`.

## Run as a service (normal configuration)

The default installation folder is `C:\GremlinEx\GEXMaestroBridge`. Run these examples from an elevated terminal in that folder:

**PowerShell:**

```powershell
# Register once, then start using the configured listen IP and default ports 8010/8011.
.\GEXMaestroBridgeService.exe --install
.\GEXMaestroBridgeService.exe --start

# Inspect and stop it.
Get-Service GEXMaestroBridgeService
.\GEXMaestroBridgeService.exe --stop

# Remove its registration when no longer needed.
.\GEXMaestroBridgeService.exe --uninstall
```

**Command Prompt:**

```cmd
rem Register once, then start using the configured listen IP and default ports 8010/8011.
GEXMaestroBridgeService.exe /install
GEXMaestroBridgeService.exe /start

rem Inspect and stop it.
sc.exe query GEXMaestroBridgeService
GEXMaestroBridgeService.exe /stop

rem Remove its registration when no longer needed.
GEXMaestroBridgeService.exe /uninstall
```

Skip `--install` when setup has already registered the service. Keep the executable and all dependencies in their final location before registering it. A service registered by `--install` uses Windows' default LocalSystem account and starts automatically with Windows.

Runtime options supplied alongside `--install` or `--start` are not saved or forwarded to the service. Edit the installed `appsettings.json` for persistent address and other settings, and restart the service to apply changes.

### Uninstall the service

From an elevated terminal, either command below stops the service if it is running and removes its registration. These examples work from any directory; adjust the installation path if needed.

**PowerShell:**

```powershell
& 'C:\GremlinEx\GEXMaestroBridge\GEXMaestroBridgeService.exe' --uninstall
```

**Command Prompt:**

```cmd
"C:\GremlinEx\GEXMaestroBridge\GEXMaestroBridgeService.exe" /uninstall
```

Unregistering the service retains the application files, `appsettings.json`, and saved device definitions. If you want to register it again later, use the install examples below.

### Install with a specific IP and port

These examples register a new service listening at `192.168.1.20:9000`, with replies sent to UDP port 9001. The IP is the **bridge computer's listen address**. Replace the example addresses with IPv4 addresses assigned to that computer, or use `0.0.0.0` to listen on all its IPv4 interfaces. Change the executable path if you installed elsewhere.

Use one shell's examples. Run the install example only when the service is not already registered; use the change examples for an existing service.

**PowerShell:**

```powershell
$startup = '"C:\GremlinEx\GEXMaestroBridge\GEXMaestroBridgeService.exe" --Osc:ListenAddress 192.168.1.20 --port 9000 --output-port 9001'
New-Service -Name GEXMaestroBridgeService -DisplayName 'GEX Maestro Bridge Service' `
    -StartupType Automatic -BinaryPathName $startup
Start-Service GEXMaestroBridgeService
```

**Command Prompt:**

```cmd
sc.exe create GEXMaestroBridgeService start= auto binPath= "\"C:\GremlinEx\GEXMaestroBridge\GEXMaestroBridgeService.exe\" --Osc:ListenAddress 192.168.1.20 --port 9000 --output-port 9001"
GEXMaestroBridgeService.exe /start
```

PowerShell uses `New-Service` to register the exact startup command. The change examples below use Windows' `Win32_Service.Change` method and check its result. In Command Prompt, the space after `binPath=` and after `start=` is required.

### Change both IP and ports

Stop the existing service, replace its startup command, then start it again. This changes the listen address to `192.168.1.21`, receive port to 9100, and reply destination port to 9101.

**PowerShell:**

```powershell
Stop-Service GEXMaestroBridgeService
$startup = '"C:\GremlinEx\GEXMaestroBridge\GEXMaestroBridgeService.exe" --Osc:ListenAddress 192.168.1.21 --port 9100 --output-port 9101'
$service = Get-CimInstance Win32_Service -Filter "Name='GEXMaestroBridgeService'"
$result = Invoke-CimMethod -InputObject $service -MethodName Change -Arguments @{ PathName = $startup }
if ($result.ReturnValue -ne 0) { throw "Service configuration failed: $($result.ReturnValue)" }
Start-Service GEXMaestroBridgeService
```

**Command Prompt:**

```cmd
GEXMaestroBridgeService.exe /stop
sc.exe config GEXMaestroBridgeService binPath= "\"C:\GremlinEx\GEXMaestroBridge\GEXMaestroBridgeService.exe\" --Osc:ListenAddress 192.168.1.21 --port 9100 --output-port 9101"
GEXMaestroBridgeService.exe /start
```

### Change the IP only

Continuing from the preceding example, change the listen address to `192.168.1.22` and retain receive/reply ports 9100/9101 by repeating them:

**PowerShell:**

```powershell
Stop-Service GEXMaestroBridgeService
$startup = '"C:\GremlinEx\GEXMaestroBridge\GEXMaestroBridgeService.exe" --Osc:ListenAddress 192.168.1.22 --port 9100 --output-port 9101'
$service = Get-CimInstance Win32_Service -Filter "Name='GEXMaestroBridgeService'"
$result = Invoke-CimMethod -InputObject $service -MethodName Change -Arguments @{ PathName = $startup }
if ($result.ReturnValue -ne 0) { throw "Service configuration failed: $($result.ReturnValue)" }
Start-Service GEXMaestroBridgeService
```

**Command Prompt:**

```cmd
GEXMaestroBridgeService.exe /stop
sc.exe config GEXMaestroBridgeService binPath= "\"C:\GremlinEx\GEXMaestroBridge\GEXMaestroBridgeService.exe\" --Osc:ListenAddress 192.168.1.22 --port 9100 --output-port 9101"
GEXMaestroBridgeService.exe /start
```

### Change the ports only

Continuing from the IP-only example, retain the listen address `192.168.1.22` and change receive/reply ports to 9200/9201:

**PowerShell:**

```powershell
Stop-Service GEXMaestroBridgeService
$startup = '"C:\GremlinEx\GEXMaestroBridge\GEXMaestroBridgeService.exe" --Osc:ListenAddress 192.168.1.22 --port 9200 --output-port 9201'
$service = Get-CimInstance Win32_Service -Filter "Name='GEXMaestroBridgeService'"
$result = Invoke-CimMethod -InputObject $service -MethodName Change -Arguments @{ PathName = $startup }
if ($result.ReturnValue -ne 0) { throw "Service configuration failed: $($result.ReturnValue)" }
Start-Service GEXMaestroBridgeService
```

**Command Prompt:**

```cmd
GEXMaestroBridgeService.exe /stop
sc.exe config GEXMaestroBridgeService binPath= "\"C:\GremlinEx\GEXMaestroBridge\GEXMaestroBridgeService.exe\" --Osc:ListenAddress 192.168.1.22 --port 9200 --output-port 9201"
GEXMaestroBridgeService.exe /start
```

Both `Win32_Service.Change` with `PathName` and `sc.exe config ... binPath=` replace the entire executable command line. Before adapting the IP-only or port-only examples, inspect the current command with `sc.exe qc GEXMaestroBridgeService` and retain your actual unchanged values and any other startup options. To change only the receive port while preserving a separate reply port, keep the existing `--output-port` value explicitly; otherwise a receive-only override selects receive port + 1.

For GremlinEx on another computer, configure `--Osc:ReplyAddress` and `--Osc:AllowedSenders:0` with the **GremlinEx computer's IP**, as in the [remote network example](#gremlinex-on-another-computer). These settings can also remain in `appsettings.json`. Update the client and firewall rules to match any new ports; the commands above configure the service startup command.

See [installer options](installer/README.md) and [upgrade behavior](installer/README-UPGRADES.md) for setup and upgrade details.

## Run from a terminal (diagnostics/testing only)

Open PowerShell or the classic Windows Command Prompt (`cmd.exe`) as Administrator for service management or real HIDMaestro devices. Run commands from the folder containing the executable and its dependencies. The installer normally uses the locations shown below.

**PowerShell:**

```powershell
Set-Location 'C:\GremlinEx\GEXMaestroBridge'
.\GEXMaestroBridgeService.exe --help
.\GEXMaestroBridgeService.exe --version
```

**Command Prompt:**

```cmd
cd /d "C:\GremlinEx\GEXMaestroBridge"
GEXMaestroBridgeService.exe /help
GEXMaestroBridgeService.exe /version
```

Command Prompt uses `%NAME%` for environment variables, `rem` for comments, and `^` to continue a command on the next line. A continuation caret must be the last character on the line, with no trailing spaces. PowerShell uses `$env:NAME`, `#`, and a backtick instead. The bridge's switch prefixes work in either shell.

To run the bridge in the console with receive port 8010 and reply destination port 8011:

**PowerShell:**

```powershell
.\GEXMaestroBridgeService.exe
```

**Command Prompt:**

```cmd
GEXMaestroBridgeService.exe
```

Press **Ctrl+C** to stop the console bridge. Stop an installed bridge service before running a console instance on the same listen address and receive port:

**PowerShell:**

```powershell
.\GEXMaestroBridgeService.exe --stop
```

**Command Prompt:**

```cmd
GEXMaestroBridgeService.exe /stop
```

## Command reference

Built-in switches accept `--`, `-`, or `/` prefixes and are case-insensitive. For example, `--version`, `-version`, and `/version` are equivalent. Use one action per invocation; action commands exit without starting a console listener.

| Action | Description |
| --- | --- |
| `--help`, `-h`, `/h`, `--h` | Print the built-in usage text and exit. |
| `--version`, `-v`, `/v`, `--v` | Print the application version and exit. |
| `--test` | Run built-in configuration, OSC, and mock-backend self-tests and exit. Creates no HID hardware. |
| `--install` | Register the current executable as an automatic Windows service. Does not start it, copy files, or configure the firewall. |
| `--uninstall` | Stop the service if necessary and remove its registration. Leaves application files and saved device definitions in place. |
| `--start` | Request startup of the registered Windows service. |
| `--stop` | Stop the registered Windows service if running. |

Runtime syntax:

```text
GEXMaestroBridgeService.exe [receivePort [outputPort]] [--mock]
GEXMaestroBridgeService.exe [--port receivePort] [--output-port outputPort] [--mock]
GEXMaestroBridgeService.exe [runtime options] [--Osc:Setting value ...]
```

| Runtime option | Aliases | Description |
| --- | --- | --- |
| `--port <number>` | `--receive-port`, `--udp-port`, `--Osc:ReceivePort` | UDP port on which the bridge receives OSC commands. Default: 8010. |
| `--output-port <number>` | `--send-port`, `--Osc:SendPort` | UDP destination port for bridge replies. The client listens on this port. Default: 8011. |
| `--mock` | `-mock`, `/mock` | Use the mock backend without creating Windows HID devices. |

Named port options accept either a space or `=` before the value:

**PowerShell:**

```powershell
# Receive on 9000; send replies to 9001.
.\GEXMaestroBridgeService.exe 9000
.\GEXMaestroBridgeService.exe --port 9000

# Receive on 9000; send replies to 9100.
.\GEXMaestroBridgeService.exe 9000 9100
.\GEXMaestroBridgeService.exe --receive-port=9000 --send-port=9100
```

**Command Prompt:**

```cmd
rem Receive on 9000; send replies to 9001.
GEXMaestroBridgeService.exe 9000
GEXMaestroBridgeService.exe /port 9000

rem Receive on 9000; send replies to 9100.
GEXMaestroBridgeService.exe 9000 9100
GEXMaestroBridgeService.exe /receive-port=9000 /send-port=9100
```

Ports must be different integers from 1 through 65535. Supplying only a receive port selects receive port + 1 for replies, even if an older output port exists in configuration. Receive port 65535 therefore requires an explicit output port. Supply each named port only once, including its aliases, and at most two positional ports.

## Network and configuration overrides

The bridge reads the required `appsettings.json` beside its executable, then applies `MAESTRO_` environment variables and command-line configuration overrides. Use `--Osc:Setting value` or `--Osc:Setting=value` for these overrides.

**Current startup behavior:** an invocation with no arguments explicitly selects ports 8010/8011, overriding port values in `appsettings.json` or the environment. Pass explicit port arguments when you need different ports. With other runtime arguments, configured ports are used unless overridden; a receive-only override at a higher configuration priority also selects receive port + 1 for replies.

### Changing service configruation using appsettings.json

Save one complete example below as `C:\GremlinEx\GEXMaestroBridge\appsettings.json`, replacing the file contents. If you installed elsewhere, use the file beside that executable. Stop the bridge before editing and restart it afterward; settings are read at startup.

You must stop and restart the service for the changes to take effect after editing the settings file.

#### Localhost with default ports

Use this when GremlinEx and the bridge run on the same computer. GremlinEx sends to `127.0.0.1:8010` and listens for replies on UDP 8011.

```json
{
  "Osc": {
    "ListenAddress": "127.0.0.1",
    "ReceivePort": 8010,
    "SendPort": 8011,
    "ReplyAddress": "127.0.0.1",
    "AllowedSenders": ["127.0.0.1"],
    "ReceiveBufferBytes": 1048576,
    "MaxDevices": 16,
    "InstallDriverOnStart": true,
    "Mock": false,
    "DefinitionsPath": ""
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    },
    "EventLog": {
      "LogLevel": { "Default": "Information" }
    }
  }
}
```

#### LAN IPv4 with custom ports

This example assumes the bridge computer owns `192.168.1.20` and GremlinEx runs on another computer at `192.168.1.50`. Replace both addresses with your actual IPv4 addresses. GremlinEx sends to `192.168.1.20:9000` and listens for replies on UDP 9001. Allow inbound UDP 9000 on the bridge computer and reply traffic on UDP 9001 on the GremlinEx computer.

```json
{
  "Osc": {
    "ListenAddress": "192.168.1.20",
    "ReceivePort": 9000,
    "SendPort": 9001,
    "ReplyAddress": "192.168.1.50",
    "AllowedSenders": ["192.168.1.50"],
    "ReceiveBufferBytes": 1048576,
    "MaxDevices": 16,
    "InstallDriverOnStart": true,
    "Mock": false,
    "DefinitionsPath": ""
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    },
    "EventLog": {
      "LogLevel": { "Default": "Information" }
    }
  }
}
```

To use the file's IP and port settings in a console session, launch with a non-port option so the current no-argument port defaults do not override the file:

**PowerShell:**

```powershell
.\GEXMaestroBridgeService.exe --Logging:LogLevel:Default Information
```

**Command Prompt:**

```cmd
GEXMaestroBridgeService.exe --Logging:LogLevel:Default Information
```

For service mode, use the service configuration examples in the first section to set the startup command to `"C:\GremlinEx\GEXMaestroBridge\GEXMaestroBridgeService.exe" --Logging:LogLevel:Default Information`. Remove any startup IP/port overrides when the JSON file should control those settings. The non-port logging option makes this invocation use the configured ports. Restart the service after saving the file or changing the startup command.

### Configuration options

| Configuration option | Description |
| --- | --- |
| `--Osc:ReceivePort <number>` | Alias of `--port`; receive UDP port, default 8010. |
| `--Osc:SendPort <number>` | Alias of `--output-port`; reply destination UDP port, default 8011. |
| `--Osc:ListenAddress <IPv4>` | Address to bind. Use `127.0.0.1` for local access or `0.0.0.0` for all IPv4 interfaces. A blank configured address resolves to a host IPv4 address. |
| `--Osc:ReplyAddress <IPv4>` | Send all replies to this address. If blank in configuration, reply to each request sender's IP. |
| `--Osc:AllowedSenders:<index> <IPv4>` | Permitted sender at a zero-based index: `:0`, `:1`, `:2`, and so on. An empty configured list allows any sender; overrides replace individual array entries. |
| `--Osc:DefinitionsPath <path>` | Saved device definitions file. Use an absolute path for consistent console and service behavior. |
| `--Osc:MaxDevices <number>` | Device limit, from 1 through 256. Default: 16. |
| `--Osc:ReceiveBufferBytes <number>` | Receive buffer size, from 65536 through 16777216 bytes. Default: 1048576. |
| `--Osc:InstallDriverOnStart <true\|false>` | Whether the real backend requests driver installation at startup. Default: true. Real device operations still require elevation when false. |
| `--Osc:Mock <true\|false>` | Select the backend through configuration. Default: false. `--mock` always forces mock mode for that invocation. |

The default definitions directory is `%ProgramData%\MaestroOscService`, using `devices.json` for real devices and `devices.mock.json` for mock devices.

### Logging options

Other configuration switches are passed through to the application's configuration system. Each requires a value, supplied after a space or `=`. The settings in the shipped `appsettings.json` include:

| Configuration option | Description |
| --- | --- |
| `--Logging:LogLevel:Default <level>` | Default log level. Shipped value: `Information`. |
| `--Logging:LogLevel:Microsoft.Hosting.Lifetime <level>` | Host startup and shutdown log level. Shipped value: `Information`. |
| `--Logging:EventLog:LogLevel:Default <level>` | Windows Event Log provider's default log level. Shipped value: `Information`. |

Levels are `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, and `None`. For example, add `--Logging:LogLevel:Default Debug` to a console command or a service's startup command to enable more detailed application logs. Boolean configuration settings also require an explicit `true` or `false`; `--mock` is a flag that needs no value.

### GremlinEx on the same computer

**PowerShell:**

```powershell
.\GEXMaestroBridgeService.exe --port 8010 --output-port 8011 `
    --Osc:ListenAddress 127.0.0.1 `
    --Osc:ReplyAddress 127.0.0.1 `
    --Osc:AllowedSenders:0 127.0.0.1
```

**Command Prompt:**

```cmd
GEXMaestroBridgeService.exe /port 8010 /output-port 8011 ^
    --Osc:ListenAddress 127.0.0.1 ^
    --Osc:ReplyAddress 127.0.0.1 ^
    --Osc:AllowedSenders:0 127.0.0.1
```

Configure GremlinEx to send to `127.0.0.1:8010` and listen for replies on UDP 8011.

### GremlinEx on another computer

This example assumes GremlinEx runs at `192.168.1.50`:

**PowerShell:**

```powershell
.\GEXMaestroBridgeService.exe --port 8010 --output-port 8011 `
    --Osc:ListenAddress 0.0.0.0 `
    --Osc:ReplyAddress 192.168.1.50 `
    --Osc:AllowedSenders:0 192.168.1.50
```

**Command Prompt:**

```cmd
GEXMaestroBridgeService.exe /port 8010 /output-port 8011 ^
    --Osc:ListenAddress 0.0.0.0 ^
    --Osc:ReplyAddress 192.168.1.50 ^
    --Osc:AllowedSenders:0 192.168.1.50
```

Configure GremlinEx to send to the bridge computer's LAN IPv4 address on UDP 8010 and listen on UDP 8011. Allow inbound UDP 8010 for the bridge in the bridge computer's firewall, and inbound replies on UDP 8011 on the GremlinEx computer. Launching the executable does not create firewall rules. Use the installer's remote-host and firewall options to configure these on the bridge computer.

Replies target the configured output port regardless of the request's source port. The reply socket uses an ephemeral source port. Only one application should listen on a given local IP and reply port.

### Environment variables

Use `__` for nested configuration keys:

**PowerShell:**

```powershell
$env:MAESTRO_Osc__DefinitionsPath = "$env:TEMP\GEXMaestroBridge-devices.mock.json"
.\GEXMaestroBridgeService.exe --mock --port 9000
Remove-Item Env:\MAESTRO_Osc__DefinitionsPath
```

**Command Prompt:**

```cmd
set "MAESTRO_Osc__DefinitionsPath=%TEMP%\GEXMaestroBridge-devices.mock.json"
GEXMaestroBridgeService.exe /mock /port 9000
set "MAESTRO_Osc__DefinitionsPath="
```

After stopping the mock bridge with Ctrl+C, run the final command to remove the temporary variable. Variables set this way apply to processes launched from that terminal session. A service started by Windows does not inherit those session variables. Use the installed `appsettings.json` for persistent service settings; custom service ports currently require startup arguments in the service's executable command line.

## Mock mode and source builds

For a console session without real HID devices, use a writable definitions file and an explicit local listener:

**PowerShell:**

```powershell
.\GEXMaestroBridgeService.exe --mock --port 9000 `
    --Osc:ListenAddress 127.0.0.1 `
    --Osc:AllowedSenders:0 127.0.0.1 `
    --Osc:DefinitionsPath "$env:TEMP\GEXMaestroBridge-devices.mock.json"
```

**Command Prompt:**

```cmd
GEXMaestroBridgeService.exe /mock /port 9000 ^
    --Osc:ListenAddress 127.0.0.1 ^
    --Osc:AllowedSenders:0 127.0.0.1 ^
    --Osc:DefinitionsPath "%TEMP%\GEXMaestroBridge-devices.mock.json"
```

The client must send to UDP 9000 and listen for replies on UDP 9001. Mock mode creates no Windows hardware. A real build can run in mock mode; a build compiled with `MockOnly=true` requires mock mode for a listener.

From the repository root, pass bridge arguments after `dotnet run`'s `--` separator. These examples use the normal build and require `lib\HIDMaestro.Core.dll`, including when selecting the mock backend:

**PowerShell:**

```powershell
dotnet run --project .\src\MaestroOscService -- --help
dotnet run --project .\src\MaestroOscService -- --test
dotnet run --project .\src\MaestroOscService -- --mock --port 9000 `
    --Osc:ListenAddress 127.0.0.1 `
    --Osc:DefinitionsPath "$env:TEMP\GEXMaestroBridge-devices.mock.json"
```

**Command Prompt:**

```cmd
dotnet run --project .\src\MaestroOscService -- /help
dotnet run --project .\src\MaestroOscService -- /test
dotnet run --project .\src\MaestroOscService -- /mock /port 9000 ^
    --Osc:ListenAddress 127.0.0.1 ^
    --Osc:DefinitionsPath "%TEMP%\GEXMaestroBridge-devices.mock.json"
```

Use a separate build output folder or clean the project when switching between mock-only and real builds. See the [main README](README.md) for build requirements and OSC client examples.

The current `MockOnly=true` source build fails with an unresolved `HIDMaestro` namespace in `Backend.cs`. Use the normal SDK-backed build with `--mock` until that build issue is resolved.

## Diagnostics

- Use `--version` to identify the executable being run and `--help` to print its built-in usage.
- A rejected startup argument or invalid OSC configuration prints an error and usage text and exits with code 2. Immediately after the command, inspect `$LASTEXITCODE` in PowerShell or run `echo %ERRORLEVEL%` on the next line in Command Prompt.
- Service-management command failures print an error and exit with code 1. Check elevation, the registered service name, and its executable path.
- If the receive port is already in use, stop the other bridge instance or choose a different receive port. If replies do not arrive, check the client's reply port, `AllowedSenders`, `ReplyAddress`, and firewall rules.
- Console sessions show logs in the terminal. For an installed service, check Windows Event Viewer under **Windows Logs > Application**.
