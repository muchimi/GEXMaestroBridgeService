# Maestro OSC Service

A standalone C# Windows Worker Service bridging OSC/UDP to HIDMaestro. Receive on **8010**; reply to **8011**. Includes named console controller profiles and custom DirectInput joysticks with **up to 128 buttons, 8 axes and 4 independent hats**.

## Status and requirements

This is a source implementation, not a precompiled Windows release. The authoring environment had no .NET SDK, Windows, or HIDMaestro binary. Therefore the C# build, C# self-tests, Windows Service lifecycle, and live HID/DirectInput behavior have **not** been executed here. Run the included validation steps on Windows before deploying. Python client checks were run; see VALIDATION.md for exact scope.

- Windows x64 (or publish for Windows ARM64 with a matching complete HIDMaestro build).
- .NET 10 SDK and a Visual Studio release supporting .NET 10 and Windows SDK 10.0.26100.0.
- A complete **HIDMaestro 2.x `HIDMaestro.Core.dll`** with embedded native payloads, from the upstream release/build. Put it in `lib/`. No SDK binary or upstream code is redistributed here.
- Administrator privileges for real controller creation; the installed service runs as LocalSystem.
- NuGet restore for Microsoft.Extensions.Hosting, WindowsServices and System.Management 10.0.0.

The bridge uses `HMContext.LoadDefaultProfiles`, `AllProfiles`, `CreateController`, `HMProfileBuilder`, `HMGamepadState`, `StandardAxes`, `SubmitState`, `SubmitRawReport`, and controller/context disposal. Upstream master was inspected on October 30, 2026; no specific upstream commit was available to pin. Use a matching 2.x release and compile to confirm binary/API compatibility.

## Open, build and run

Open `MaestroOscService.sln` in Visual Studio Professional. Place the complete SDK DLL into `lib/`, restore packages and build. Start Visual Studio elevated when debugging real devices. There is no GUI; console logging appears while debugging, and the installed service logs to Windows Event Log.

From PowerShell in this directory:

```powershell
dotnet build .\MaestroOscService.sln -c Release
dotnet run --project .\src\MaestroOscService -- --self-test
dotnet run --project .\src\MaestroOscService
```

To build/test the protocol without the HIDMaestro DLL:

```powershell
dotnet run --project .\src\MaestroOscService -p:MockOnly=true -- --self-test
dotnet run --project .\src\MaestroOscService -p:MockOnly=true -- --mock
```

Mock mode exposes `mock-gamepad` plus custom joystick creation but creates no Windows hardware. To keep test definitions in a writable private location, set this before running mock mode:

```powershell
$env:MAESTRO_Osc__DefinitionsPath = "$env:TEMP\MaestroOscService-devices.mock.json"
```

Use separate build output folders or clean the project when switching between MockOnly and real builds. A real build can also run `--mock`; a MockOnly build cannot run real controllers.

## Transport

`src/MaestroOscService/appsettings.json` controls addresses and ports:

```json
{
  "Osc": {
    "ListenAddress": "127.0.0.1",
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

Defaults permit local clients only. For LAN access, set ListenAddress to the server's interface IP (or 0.0.0.0), explicitly add the client's IP to AllowedSenders, and allow inbound UDP 8010 for the service executable in Windows Firewall. Configure the client firewall to receive replies on 8011. These are IPv4 literals. This is a local/trusted-LAN control interface, without authentication or encryption.

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

Definitions are written atomically to `%ProgramData%\MaestroOscService\devices.json`; mock mode uses `devices.mock.json`. Set Osc:DefinitionsPath to override. Enabled definitions are recreated on startup with stable identity keys `MaestroOscService:{deviceId}` and neutral state. Deactivate marks the definition disabled; delete erases it. Controller input states are not persisted. Corrupt definitions or a failed startup recreation stop startup instead of silently accepting a partial configuration.

`/maestro_definitions` lists service-defined devices, `/maestro_devices` lists active service-owned devices, and `/maestro_profiles` lists emulation templates. `/maestro_system_devices` is a separate broad, present Windows HID inventory and may include keyboards/mice or related HID interfaces. It is not an exact list of virtual game controllers. PnP IDs and devices owned by another process cannot be passed to control commands; the service does not take over another HIDMaestro context's devices.

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
dotnet run --project .\src\MaestroOscService -p:MockOnly=true -- --self-test
```

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

From the project root, publish an x64 self-contained folder:

```powershell
dotnet publish .\src\MaestroOscService -c Release -r win-x64 --self-contained true -o .\publish
```

Keep the entire publish folder together, including HIDMaestro.Core.dll and appsettings.json. This bridge does not currently request trimming, NativeAOT or single-file publishing.

Run elevated:

```powershell
.\scripts\install-service.ps1 -PublishDirectory .\publish
Get-Service MaestroOscService
Stop-Service MaestroOscService
Start-Service MaestroOscService
```

The install script copies to Program Files, restricts write access to Administrators/SYSTEM, secures the ProgramData definitions directory, registers the service and starts it. It does not install a firewall rule because the default listener is loopback. Stop/uninstall an existing deployment before replacing its files. Service restart recreates enabled definitions, while service stop disposes owned controllers.

```powershell
.\scripts\uninstall-service.ps1
```

Uninstall removes the service registration; published files and saved definitions are retained.

## Sources

- https://github.com/hifihedgehog/HIDMaestro
- https://github.com/hifihedgehog/HIDMaestro/blob/master/sdk/HIDMaestro.Core/HMContext.cs
- https://github.com/hifihedgehog/HIDMaestro/blob/master/sdk/HIDMaestro.Core/HMProfileBuilder.cs
- https://github.com/hifihedgehog/HIDMaestro/blob/master/sdk/HIDMaestro.Core/HMGamepadState.cs
- https://github.com/hifihedgehog/HIDMaestro/blob/master/example/SdkDemo/Program.cs
- https://opensoundcontrol.stanford.edu/spec-1_0.html
