# GEX Maestro Bridge Service

A standalone C# Windows Worker Service bridging [GremlinEx (GEX)](https://github.com/muchimi/JoystickGremlinEx) to [HIDMaestro](https://github.com/hifihedgehog/HIDMaestro).

The module provides an interface to HID Maestro's API to manage joystick and gamepad devices and to read/write to/from them.

The interface is based on the standard OSC (Open Sound Control) protocol, for two reasons.  First, OSC support is built-in GremlinEx and lets it easily connect to this bridge to manage virtual HID Maestro devices with existing APIs.  Second, it is lightweight and fast and supports multiple parameter types and handshake information for two way communication.   While this interface is created specifically to bridge GremlinEx Python/C++ platform to HIDMaestro's C#/.NET platform, because it is an implementation of the OSC protocol it can be used with any OSC compatible software for the purpose of sending buttons, hats and axis data to HIDMaestro.

## Implementation Overview

The module is designed to run as a Windows service, and for diagnostics purposes, can also run as a console application.

Because HID Maestro requires elevated privileges, so does this module.

The service can be installed on the same machine as GremlinEx, or a different machine.

No separate installation of HIDMaestro is required as the bridge module encapsulates that functionality.

## GEX Integration

The bridge module should function with any application, however it is specifically setup to work with [GremlinEx (GEX)](https://github.com/muchimi/JoystickGremlinEx) as a replacement option to VJOY and VIGEM.

GEX manages the Maestro joystick and gamepad creation to ensure compatibility with GEX and automatic device mapping from older VJOY to the new Maestro equivalents created by GEX.

## Capabilities

The focus of the bridge module is only to work with joystick and gamepads.  It is not designed to work with other devices supported by HID Maestro as the focus is only for the mapping of joystick and gamepad devices.

There is no limit imposed by HID Maestro on the number of joysticks, no more than 16 is recommended, and a practical count of 4 is recommended and will be setup by GEX as a base configuration, matching the maximum DINPUT specification of 128 buttons, 8 axes and 4 hats, x4 devices.

There is a hard limit on gamepad devices as the XINPUT standard limits this to four (4) devices (player 1 through 4).

The bridge aims to keep the created device deterministic by relative position, meaning the first device created by GEX will be the first one in the Windows device list, the second device will appear after the first device but not necessarily sequentially.  The topic of how Windows orders controllers is out of scope however the bridge aims to keep the sequence as predictable as possible without any ability to manage the operating system's ordering process as this capability is not exposed.

## OSC configuration

The bridge receives command on the receive UDP port, default **8010**.  The send module (clients will listen to this port for reply messages from the bridge) defaults to **8011**. If the reply ports is not provided, it defaults to the listen port + 1.

## General features

The bridge module implements OSC commands that are all prefixed with **/maestro_**, followed by optional parameters, which can be integer, floating point, or string data, the later is usually using JSON formatted string data to pass complex data back and forth.

## Joystick and Game pad (console controller) support

The bridge supports all joystick and gamepad devices defined in HID Maestro.

The bridge also adds a custom joystick device built on the Microsoft DINPUT specification which is extensively used by GEX.  This specification calls for normal joysticks to have up to 128 buttons, 8 axes and 4 hats.  This is the maximum, the actual devices can have fewer inputs, and most do, especially the emulated devices.

Furthermore, the bridge is configured to output standard axis, button and hat data to joysticks or gamepads, to facilitiate the use of GEX's extensive joystick mapping capabilities without having to treat gamepads that use XINPUT as different devices.


## Requirements and dependencies

This is a source implementation, not a precompiled Windows release.  The precompiled release is included with GremlinEx's distribution and package.  The released package includes all dependencies.

### Dependencies:

- Windows x64 (or publish for Windows ARM64 with a matching complete HIDMaestro build).
- .NET 10 SDK and a Visual Studio release supporting .NET 10 and Windows SDK 10.0.26100.0.
-  **HIDMaestro 2.x `HIDMaestro.Core.dll`** with embedded native payloads
- Administrator privileges for real controller creation (non mock mode); the installed service runs as LocalSystem.  The requirement for elevated privileges stems from the usage as a service and is a core requirement of HIDMaestro due to the underlying Microsoft API user mode driver requirements.  Note that elevated rights are needed both for service mode, and console mode.
- NuGet restore for Microsoft.Extensions.Hosting, WindowsServices and System.Management 10.0.0.



## Building instructions

Open `MaestroOscService.sln` in Visual Studio 2026 (Community edition is fine). Place the complete SDK DLL into `lib/` (download from [HIDMaestro](https://github.com/hifihedgehog/HIDMaestro), restore packages and build.

Start Visual Studio with elevated permissions when debugging real devices as non-mock mode will load the HID Maestro engine. There is no user interface; console logging appears while debugging, and the installed service logs to Windows Event Log.

Console mode is provided to see the traffic with OSC clients and responses.  The normal operating mode is to run this as a service that starts and stops with Windows.

To build via PowerShell in this directory:

```powershell
dotnet build .\MaestroOscService.sln -c Release
dotnet run --project .\src\MaestroOscService -- --test
dotnet run --project .\src\MaestroOscService
```

To build/test the protocol without the HIDMaestro DLL:

```powershell
dotnet run --project .\src\MaestroOscService -p:MockOnly=true -- --test
dotnet run --project .\src\MaestroOscService -p:MockOnly=true -- --mock
```

Mock mode exposes `mock-gamepad` plus custom joystick creation but creates no Windows hardware. To keep test definitions in a writable private location, set this before running mock mode:

```powershell
$env:MAESTRO_Osc__DefinitionsPath = "$env:TEMP\MaestroOscService-devices.mock.json"
```

Use separate build output folders or clean the project when switching between MockOnly and real builds. A real build can also run `--mock`; a MockOnly build cannot run real controllers.

## EXE Startup options

See the [command-line guide](COMMAND-LINE.md) for the full option reference, UDP port rules, local and remote examples, service management, and mock-mode commands.

Switch prefix can be `--`, `-`, or `/`.

- `version` (`v`) - print version and exit
- `help` (`h`) - print usage and exit
- `test` - run built-in self-tests and exit
- `install` - install Windows service
- `uninstall` - stop (if running) and remove Windows service
- `start` - start Windows service
- `stop` - stop Windows service if running
- `mock` - run in mock backend mode (no HID hardware creation)

## Transport

`src/MaestroOscService/appsettings.json` controls addresses and ports:

```json
{
  "Osc": {
    "ListenAddress": "",
    "ReceivePort": 8010,
    "SendPort": 8011,
    "ReplyAddress": "",
    "AllowedSenders": ["127.0.0.1"],
    "ReceiveBufferBytes": 1048576,
    "MaxDevices": 16,
    "InstallDriverOnStart": true,
    "Mock": false,
    "DefinitionsPath": ""
  }
}
```

Replies go to the sender's IP at destination UDP port 8011, irrespective of the request's source port. The reply socket has an ephemeral source port. A nonempty ReplyAddress overrides the destination IP for all clients. The other application must bind its reply listener to port 8011. Only one application should own that port per local IP.

By default, an empty ListenAddress resolves to the host's primary IPv4 address at startup. For LAN access from specific clients, explicitly add each client's IP to AllowedSenders and allow inbound UDP 8010 for the service executable in Windows Firewall. Configure the client firewall to receive replies on 8011. These are IPv4 literals. This is a local/trusted-LAN control interface, without authentication or encryption.

**IMPORTANT** Changes to the configuration requires a service restart for the new settings to take place.

### Example of the JSON for a local configuration (bridge runs on the same machine as GEX)

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
      "LogLevel": {
        "Default": "Information"
      }
    }
  }
}
```

In this scenario, GEX is configured to also use localhost, and sends on port 8010, and receives on port 8011.
The bridge sends on 8011 and receives on 8010.

When using localhost (127.0.0.1), no Windows Firewall rules need to be set.

### Example of the JSON for a networked configuration (bridge runs on a different machine from GEX) - advanced setup


```json
{
  "Osc": {
    "ListenAddress": "192.168.2.210",
    "ReceivePort": 8010,
    "SendPort": 8011,
    "ReplyAddress": "192.168.2.205",
    "AllowedSenders": ["192.168.2.205"],
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
      "LogLevel": {
        "Default": "Information"
      }
    }
  }
}
```

In this scenario, GEX is configured to send to 192.168.2.210, and the bridge is configured to reply back to the GEX host machine (192.168.2.205).
Windows firewall must allow UDP inbound traffic on both machines, meaning that on the bridge machine, it must allow traffic from 192.168.2.205 on port 8010, and conversely on the GEX machine, the firewall must allow inbound UDP traffic coming from the bridge machine 192.168.2.210 on port 8011.

This is an advanced scenario and is atypical as localhost is meant to be the default setup.

## Control messages: first argument is the value

The device and control selector are in the OSC address. Device IDs are stable, case-sensitive ASCII strings containing letters, digits, `_` or `-`, 1–64 characters. A display name can contain spaces and is separate from the ID. All protocol addresses, requests and responses start with `/maestro_`.

| OSC address | Write: first and only argument | Read request | Successful response |
| --- | --- | --- | --- |
| `/maestro_axis/{id}/{axis}` | Number 0..1 | Same address, no arguments | Same address, float value 0..1 |
| `/maestro_button/{id}/{button}` | Numeric 0 releases; any finite nonzero value presses | Same address, no arguments | Same address, integer 0 or 1 |
| `/maestro_hat/{id}/{hat}` | Integer-valued number 0..8 | Same address, no arguments | Same address, integer 0..8 |
| `/maestro_gamepad_axis/{id}/{role}` | Number 0..1 | Same address, no arguments | Same address, float value 0..1 |

Every successful write echoes the resulting value, so read and write replies have the same format. Control messages have **no request ID argument**. Keep at most one outstanding read/write per control address if response correlation matters. OSC booleans are also accepted for buttons. Axis values outside 0..1, fractional hat positions, NaN and infinity are rejected. Hat inputs may be OSC int32, float32 or double when exactly integral.

Axes on custom joysticks are the first N names in `X, Y, Z, Rx, Ry, Rz, Slider, Dial`; all are centered at 0.5. For catalog HID devices, use the profile's available axis names. For console pads prefer the canonical roles `LX, LY, RX, RY, LT, RT`, resolved through HIDMaestro's profile mapping. Sticks use 0.5 as center; triggers use 0 as released. A profile may omit a role or ignore a logical input absent from its emulated hardware. Changing one control retains all the others.

Buttons on custom joysticks use **1-based descriptor button numbers**, up to 128. Catalog/console profile numeric buttons 1..32 refer to HMButton logical bit positions, not necessarily native descriptor numbering. Named buttons are preferable for console pads: `A`, `B`, `X`, `Y`, `LeftBumper`, `RightBumper`, `Back`, `Start`, `LeftStick`, `RightStick`, `Guide`, and the names in the current SDK. PlayStation aliases `Cross`, `Circle`, `Square`, `Triangle` are accepted. Not every profile has every named button.

Hats use **1-based hat indices**: 1..configured count for custom joysticks; 1 for a console D-pad. The protocol values are:

| Value | Position |
| --- | --- |
| 0 | Center |
| 1 | North / Up |
| 2 | North-east |
| 3 | East / Right |
| 4 | South-east |
| 5 | South / Down |
| 6 | South-west |
| 7 | West / Left |
| 8 | North-west |

Examples (notation only; transmit OSC binary packets, not these text lines):

```text
/maestro_axis/stick1/X             0.75
/maestro_button/stick1/128         -0.25
/maestro_button/stick1/128         0
/maestro_hat/stick1/4              8
/maestro_hat/stick1/4              0
/maestro_gamepad_axis/pad1/LX      0.5
/maestro_gamepad_axis/pad1/RT      1.0
/maestro_button/pad1/Cross         1
/maestro_hat/pad1/1                3
```

To query the fourth hat, send `/maestro_hat/stick1/4` with an empty OSC type tag string `,` and no arguments. A response might be `/maestro_hat/stick1/4` with integer `8`. Queries report the service's retained requested state, not physical-device readings or proof that a game consumed the report. Full state and capabilities are available below. For custom joysticks, full-state JSON includes `pressedButtons` (1-based indices across all 128 buttons) and `hats` (all configured hats in 0..8 form). The legacy numeric `buttons` field covers only the lowest 32 bits; use `pressedButtons` for the complete set.

## Device management and discovery

Management requests use a nonnegative OSC int32 `requestId` first. This is separate from value-first control messages. Give each new management request a new ID; retry an identical request with its original ID from the same source UDP socket. Responses carry the ID first. The replay cache lasts up to 30 seconds / 2048 requests across senders. Reusing an ID for different content within that window returns request_id_conflict. Restart clears the cache.

| Request address | Arguments, in order | Reply |
| --- | --- | --- |
| `/maestro_ping` | requestId | `/maestro_pong` requestId, service name, protocol version |
| `/maestro_profiles` | requestId [, offset, limit] | Catalog profile rows and completion |
| `/maestro_definitions` | requestId [, offset, limit] | All saved definitions, including inactive devices |
| `/maestro_devices` | requestId [, offset, limit] | Active devices owned by this service |
| `/maestro_system_devices` | requestId [, offset, limit] | Read-only Windows PnP HID inventory |
| `/maestro_create` | requestId, deviceId, profileId [, displayName] | `/maestro_created` requestId, device JSON |
| `/maestro_create_custom` | requestId, deviceId, displayName, buttons, axes, hats | `/maestro_created` requestId, device JSON |
| `/maestro_rename` | requestId, deviceId, newDisplayName | `/maestro_renamed` requestId, updated definition JSON |
| `/maestro_remove` | requestId, deviceId | Deactivate, keep definition; `/maestro_ok` |
| `/maestro_activate` | requestId, deviceId | Recreate saved device; `/maestro_created` |
| `/maestro_delete` | requestId, deviceId | Remove active hardware and saved definition; `/maestro_ok` |
| `/maestro_capabilities` | requestId, deviceId | `/maestro_capabilities_result` requestId, deviceId, JSON |
| `/maestro_state` | requestId, deviceId | `/maestro_state_result` requestId, deviceId, JSON |
| `/maestro_reset` | requestId, deviceId | All controls neutral; `/maestro_ok` requestId, command address |

`create_custom` accepts 0..128 buttons, 0..8 axes, and 0..4 hats, with at least one control. To get the maximum layout:

```text
/maestro_create_custom  100  "stick1"  "Flight Controls"  128  8  4
/maestro_create         101  "pad1"    "xbox-360-wired"  "My Xbox Pad"
/maestro_create         102  "ps5"     "dualsense"        "My PS5 Pad"
/maestro_rename         103  "stick1"  "Pilot Controls"
/maestro_definitions    104
/maestro_devices        105
/maestro_delete         106  "stick1"
```

Console emulation is selected through a catalog profile; discover the installed SDK's exact profile IDs rather than assuming all examples exist in every build. Xbox, PlayStation and Nintendo personas share the same UDP control interface, with profile-specific encoding left to HIDMaestro. This means emulated console controller personalities on Windows; it does not connect the PC to a physical Xbox/PlayStation/Switch console. XInput retains its own four-player limit for Xbox-family profiles.

A repeated create for the same ID and definition returns the existing device. Reusing an ID for a different layout/profile/name requires rename or delete/recreate. Display names are 1–128 characters. An active rename removes/recreates that controller, retains its service ID and durable identity key, and resets input to neutral; a connected game may see a hot-plug. Windows/DirectInput may cache an OEM name associated with VID/PID. The bridge sets per-profile Name/ProductString/DeviceDescription and does not overwrite a machine-wide OEM label shared by other controllers.

Lists are paged, default offset 0 and limit 100 (maximum 200). Each row is a separate OSC datagram:

```text
/maestro_profile        requestId, absoluteRowIndex, profileJson
/maestro_definition     requestId, absoluteRowIndex, definitionJson
/maestro_device         requestId, absoluteRowIndex, deviceJson
/maestro_system_device  requestId, absoluteRowIndex, pnpJson
/maestro_done           requestId, originalRequestAddress, totalRows, offset, returnedRows
```

Reassemble by requestId/index; packets may arrive out of order and a completion packet may arrive before rows. If rows are missing, retry the identical request from the same socket within the replay window. For a later page, use a new request ID. Inventory can change between pages; there is no snapshot across different requests.

Errors are `/maestro_error` with `[requestId, originalAddress, errorCode, description]`. For control messages and malformed packets requestId is -1. Unknown addresses, wrong types, invalid ranges, unknown devices and backend errors are reported without a stack trace.

## Saved definitions and device ownership

Definitions are written atomically to `%ProgramData%\MaestroOscService\devices.json`; mock mode uses `devices.mock.json`. Set Osc:DefinitionsPath to override. Enabled definitions are recreated on startup with stable identity keys and neutral state. Managed joysticks are restored numerically (`gex-1`, `gex-2`, ..., `gex-16`), before other devices. Deactivate marks the definition disabled; delete erases it. Controller input states are not persisted. Corrupt definitions or a failed startup recreation stop startup instead of silently accepting a partial configuration.

`/maestro_definitions` lists service-defined devices, `/maestro_devices` lists active service-owned devices, and `/maestro_profiles` lists emulation templates. `/maestro_system_devices` is a separate broad, present Windows HID inventory and may include keyboards/mice or related HID interfaces. It is not an exact list of virtual game controllers. PnP IDs and devices owned by another process cannot be passed to control commands; the service does not take over another HIDMaestro context's devices.

### GEX joystick identity and Windows order

Canonical service IDs `gex-1` through `gex-16` use versioned, deterministic identity keys. Each key is chosen so the SDK's hashed instance token and HID parent prefix sort in numeric GEX order. The identity depends on the service ID, independent of the active device count, display name, layout, or Maestro transport slot. Other service IDs retain their previous identity namespaces. Saved generated labels such as a second `gex-1` on service ID `gex-2` are repaired on restoration; custom display names are retained.

Active device responses expose `identityKey`, `instanceToken`, `hidParentIdPrefix`, and `failoverVjoyId`. Updated GremlinEx correlates these tokens against a fresh local HIDAPI scan, preserving the observed Windows order and counting active bridge joysticks even when they are unselected in Gremlin. Automatic local failover requires verified relative order: `gex-2` must follow `gex-1`, `gex-3` must follow `gex-2`, and so on. Other controllers may occur between GEX devices. An active controller absent from the HID scan, ambiguous matches, scan errors, and out-of-order devices prevent automatic failover while explicit Maestro device mappings remain usable. Remote bridge devices have no local Windows enumeration to verify.

The failover mapping remains `vJoy N -> gex-N`, regardless of the Windows enumeration position. Inactive IDs may leave gaps: active `gex-2` and `gex-4` are valid if they enumerate in that order, and still map to vJoy 2 and 4. Gamepads and devices owned by other Maestro clients are outside this GEX joystick ordering policy. Maestro transport indexes and Gremlin's saved slots are separate identifiers.

**Upgrade:** these ordered keys replace the old keys for managed GEX joysticks. Rebuild and restart the bridge to recreate existing controllers with their new instance paths. Windows GUIDs, game bindings, and HIDHide rules tied to the old paths may need to be updated. Subsequent restarts keep the new keys stable. Use the updated bridge and GremlinEx together; older bridges do not supply the identity metadata needed for HID order verification.

## How the full joystick layout works

`CustomJoystick.cs` builds a standard HID Joystick Application Collection and packs reports itself:

- Configured button count: one bit per button, then zero padding to the next byte.
- Configured axis count: signed little-endian 16-bit axes in X/Y/Z/Rx/Ry/Rz/Slider/Dial order, -32767..32767.
- Configured hat count: one byte per independent POV, HID logical values 0..7 and null 255.
- No HID Report ID. Maximum data report size is 16 + 16 + 4 = **36 bytes**.
- Wire conversion translates protocol hat 0 to HID null 255; protocol 1..8 becomes HID 0..7.

The profile is built with HMProfileBuilder and sent through `SubmitRawReport`, bypassing the high-level gamepad state's 32-bit button mask and single-hat limit. A custom joystick does not become an XInput pad. Use a console catalog profile when XInput or console identity/behavior is required. Custom VID/PID are currently implementation constants FEED:8010 for this prototype; allocate/configure suitable IDs before distributing a product.

## Protocol boundaries

Supports OSC messages and nested **immediate** bundles (timetag 1), int32/float32/string, boolean, int64 and double parsing. Blob/array arguments, OSC address wildcards and future timetags are not implemented. Limits: 60,000 bytes per datagram, 256 messages per bundle, nesting depth 8, 64 arguments per message, 512 characters per address. Packets are fully decoded before any command executes. Valid bundle commands run in order without interleaving another incoming packet, but are not a rollback transaction if one command fails.

UDP is best effort: no reliable delivery or ordering guarantee. Writes are absolute values, so retransmitting the same write is idempotent, but an old delayed packet can overwrite a newer value. Control writes are not sequence-numbered. Send periodic state refreshes if needed and query to reconcile; use a reliable transport or a future sequencing extension if strict edge ordering is required. Values remain in effect until changed, reset, removed or service exit; there is no automatic disconnect timeout in this version.

## Validation on Windows

C# tests (no hardware):

```powershell
dotnet run --project .\src\MaestroOscService -p:MockOnly=true -- --test
```

Run the focused identity/restoration checks independently of the full self-test suite:

```powershell
dotnet run --project .\tests\IdentityChecks -p:BuildInstaller=false -p:SkipBridgeBuildOutput=true
```

This compares all 16 derived identities with the installed SDK without creating HID devices. Add `-p:MockOnly=true` to run without the SDK; SDK comparisons are skipped in that mode. A passing check establishes the key policy; the live GremlinEx HID scan verifies the observed Windows order.

In a second terminal while the service runs in mock mode:

```powershell
python .\tests\osc_client.py --smoke
python .\tests\osc_client.py /maestro_definitions
python .\tests\osc_client.py /maestro_profiles 0 20
```

The Python client inserts request IDs for management messages only. Pass values as JSON literals; control paths need no string arguments:

```powershell
python .\tests\osc_client.py /maestro_axis/stick1/X 0.75
python .\tests\osc_client.py /maestro_axis/stick1/X
python .\tests\osc_client.py /maestro_button/stick1/128 1
python .\tests\osc_client.py /maestro_hat/stick1/4 8
```

Use your OSC application's create command examples above for hardware creation. Python string arguments must include JSON quotes (PowerShell quoting varies by version); using the Client class directly from Python also avoids shell quoting issues.

Run the real build elevated, create a custom device named `Flight Controls` with 128/8/4, then verify what DirectInput actually enumerates:

```powershell
python .\tests\verify_dinput.py --name "Flight Controls" --buttons 128 --axes 8 --hats 4
```

This DirectInput check queries Windows capabilities; it is distinct from reading bridge configuration. Test button 128, Dial endpoints and all four hats independently in a DirectInput viewer/target game. Also create an Xbox/PlayStation profile and test named buttons, both sticks and both triggers. Confirm rename, deactivate/reactivate, deletion, service stop and restart behavior. joy.cpl may display fewer controls than the device descriptor offers; the DirectInput verifier checks the API counts directly.

## Publish and install

Release builds can optionally create `GEXMaestroBridgeSetup-<version>-x64.exe` with `dotnet build -c Release -p:BuildInstaller=true`. Use `-p:PublishGitHubRelease=true` to also publish the setup to GitHub under a tag matching the project's `Version` (for example, `0.1.1`). See [installer build and release options](installer/README.md) for requirements, authentication, and output locations.

From the project root, publish an x64 self-contained folder:

```powershell
dotnet publish .\src\MaestroOscService -c Release -r win-x64 --self-contained true -o .\publish
```

Keep the entire publish folder together, including HIDMaestro.Core.dll and appsettings.json. This bridge does not currently request trimming, NativeAOT or single-file publishing.

Run elevated:

```powershell
.\scripts\install-service.ps1 -PublishDirectory .\publish
Get-Service GEXMaestroBridgeService
Stop-Service GEXMaestroBridgeService
Start-Service GEXMaestroBridgeService
```

The install script copies to `C:\GremlinEx\GEXMaestroBridge`, restricts write access to Administrators/SYSTEM, secures the ProgramData definitions directory, registers the service and starts it. It does not install a firewall rule; configure one for LAN access. Stop/uninstall an existing deployment before replacing its files. Service restart recreates enabled definitions, while service stop disposes owned controllers.

```powershell
.\scripts\uninstall-service.ps1
```

The same service scripts can be run from Windows Command Prompt:

```cmd
scripts\install-service.cmd -PublishDirectory ".\publish"
scripts\uninstall-service.cmd
```

Command Prompt launchers are also available for the installer build, release, configuration, upgrade, and validation scripts. See [Windows Command Prompt scripts](installer/README.md#windows-command-prompt-scripts).

Uninstall removes the service registration; published files and saved definitions are retained.

## Sources

- https://github.com/hifihedgehog/HIDMaestro
- https://github.com/hifihedgehog/HIDMaestro/blob/master/sdk/HIDMaestro.Core/HMContext.cs
- https://github.com/hifihedgehog/HIDMaestro/blob/master/sdk/HIDMaestro.Core/HMProfileBuilder.cs
- https://github.com/hifihedgehog/HIDMaestro/blob/master/sdk/HIDMaestro.Core/HMGamepadState.cs
- https://github.com/hifihedgehog/HIDMaestro/blob/master/example/SdkDemo/Program.cs
- https://opensoundcontrol.stanford.edu/spec-1_0.html
- https://github.com/muchimi/JoystickGremlinEx

## Change log

### 0.1.0

Initial internal release.

### 0.1.1

- Bug fix and naming conventions, gamepad support.

### 0.1.2

- Align bridge API and GEX device naming to Windows device order as much as possible to show GEX created devices in the deterministic order supported by HID Maestro.
