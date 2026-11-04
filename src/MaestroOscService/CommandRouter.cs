/*
 
 Gremlin Ex and the Gremlin Ex Bridge Service for HIDMaestro is (C) EMCS 2026 under GPL 3.0


 HIDMaestro is under a MIT Open Source license and (C) 2026 HIDMaestro Contributors
 Project details: (https://github.com/hifihedgehog/HIDMaestro) and https://hidmaestro.org/

 Please note that GremlinEx does not distribute HIDMaestro components or any of its dependencies. 
 You must obtain the HIDMaestro SDK and distributables from the HIDMaestro project.
 

 This program is free software: you can redistribute it and/or modify
 it under the terms of the GNU General Public License as published by
 the Free Software Foundation, either version 3 of the License, or
 (at your option) any later version.

 This program is distributed in the hope that it will be useful,
 but WITHOUT ANY WARRANTY; without even the implied warranty of
 MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 GNU General Public License for more details.

 You should have received a copy of the GNU General Public License
 along with this program.  If not, see <http://www.gnu.org/licenses/>.
 
  
 */



using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace GEXMaestroBridgeService;

public sealed class CommandRouter(IMaestroBackend backend, int maxDevices,
    Func<IReadOnlyList<object>> systemInventory, DefinitionCatalog? definitions = null)
{
    private readonly DefinitionCatalog _definitions = definitions ?? new(backend, maxDevices);
    private sealed record Cached(string Fingerprint, long Created, OscMessage[] Replies);
    private readonly Dictionary<(string Sender, int Id), Cached> _cache = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string ToJson(object value) => JsonSerializer.Serialize(value, Json);

    public IReadOnlyList<OscMessage> Handle(OscMessage command, string sender)
    {
        if (Environment.UserInteractive)
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Received command from {sender}: {command.Address} ({command.Arguments.Length} args) [{FormatArguments(command.Arguments)}]");
        if (command.Address.StartsWith("/maestro_axis/", StringComparison.Ordinal) ||
            command.Address.StartsWith("/maestro_button/", StringComparison.Ordinal) ||
            command.Address.StartsWith("/maestro_hat/", StringComparison.Ordinal) ||
            command.Address.StartsWith("/maestro_gamepad_axis/", StringComparison.Ordinal))
            return HandleControl(command);
        int id = command.Arguments.FirstOrDefault() is int n && n >= 0 ? n : -1;
        if (id < 0) return [Error(-1, command.Address, "bad_arguments", "First argument must be a nonnegative OSC int32 request ID")];
        var key = (sender, id);
        long now = Stopwatch.GetTimestamp();
        // Bounded replay cache: 30 seconds or 2048 requests, whichever comes first.
        foreach (var expired in _cache.Where(p => Stopwatch.GetElapsedTime(p.Value.Created, now).TotalSeconds > 30).Select(p => p.Key).ToArray())
            _cache.Remove(expired);
        string fingerprint = Convert.ToHexString(SHA256.HashData(OscCodec.Encode(command)));
        if (_cache.TryGetValue(key, out var previous))
            return previous.Fingerprint == fingerprint ? previous.Replies
                : [Error(id, command.Address, "request_id_conflict", "Request ID was already used for a different command")];
        OscMessage[] replies;
        try { replies = Dispatch(command, id).ToArray(); }
        catch (Exception ex)
        {
            string code = ex switch { ArgumentException => "bad_arguments", KeyNotFoundException => "not_found",
                InvalidOperationException => "invalid_operation", _ => "backend_error" };
            // Do not return stack traces over the network.
            replies = [Error(id, command.Address, code, ex.Message.Length > 1500 ? ex.Message[..1500] : ex.Message)];
        }
        if (_cache.Count >= 2048) _cache.Remove(_cache.MinBy(p => p.Value.Created).Key);
        _cache[key] = new(fingerprint, now, replies);
        return replies;
    }

    private IEnumerable<OscMessage> Dispatch(OscMessage m, int id)
    {
        if (!m.Address.StartsWith("/maestro_", StringComparison.Ordinal))
            throw new ArgumentException("Every command address must start with /maestro_");
        switch (m.Address)
        {
            case "/maestro_ping":
                Count(m, 1);
                return [new("/maestro_pong", id, "MaestroOscService", 1)];
            case "/maestro_profiles":
                return Page(m, id, backend.Profiles().Cast<object>().ToArray(), "/maestro_profile");
            case "/maestro_devices":
            {
                var devices = backend.Devices();
                if (Environment.UserInteractive)
                {
                    Console.WriteLine($"/maestro_devices returned {devices.Count} device(s):");
                    foreach (var d in devices)
                        Console.WriteLine($"  id={d.Id}, profileId={d.ProfileId}, name={d.Name}, transportIndex={d.TransportIndex}, controllable={d.Controllable}");
                }
                return Page(m, id, devices.Cast<object>().ToArray(), "/maestro_device");
            }
            case "/maestro_definitions":
            {
                var definitions = _definitions.Definitions();
                if (Environment.UserInteractive)
                {
                    Console.WriteLine($"/maestro_definitions returned {definitions.Count} definition(s):");
                    foreach (var d in definitions)
                        Console.WriteLine($"  id={d.Id}, kind={d.Kind}, profileId={d.ProfileId}, name={d.Name}, buttons={d.Buttons}, axes={d.Axes}, hats={d.Hats}, vid={d.VendorId}, pid={d.ProductId}, enabled={d.Enabled}");
                }
                return Page(m, id, definitions.Cast<object>().ToArray(), "/maestro_definition");
            }
            case "/maestro_system_devices":
                return Page(m, id, systemInventory(), "/maestro_system_device");
            case "/maestro_create":
                if (m.Arguments.Length != 3 && m.Arguments.Length != 4) throw new ArgumentException("Create arguments: requestId, deviceId, profileId [, displayName]");
                string deviceId = Text(m, 1), profileId = Text(m, 2);
                return [new("/maestro_created", id, ToJson(_definitions.DefineAndActivate(
                    new(deviceId, "profile", profileId, m.Arguments.Length == 4 ? Text(m, 3) : backend.Profiles().FirstOrDefault(p => p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase))?.Name ?? profileId))))];
            case "/maestro_create_custom":
                // params: requestId, deviceId, displayName, buttonCount, axisCount, hatCount [, vendorId, productId, overwrite, allowGamepad]
                if (m.Arguments.Length is < 6 or > 10)
                    throw new ArgumentException("Create custom arguments: requestId, deviceId, displayName, buttonCount, axisCount, hatCount [, vendorId, productId, overwrite, allowGamepad]");
                {
                    string customId = Text(m, 1);
                    string customName = Text(m, 2);
                    int buttonCount = Integer(m, 3);
                    int axisCount = Integer(m, 4);
                    int hatCount = Integer(m, 5);
                    int vendorId = m.Arguments.Length >= 7 ? Integer(m, 6) : 0x001;
                    int productId = m.Arguments.Length >= 8 ? Integer(m, 7) : 0x001;
                    bool overwrite = m.Arguments.Length >= 9 && Boolean(m, 8);
                    var definition = new DeviceDefinition(customId, "custom", "custom-" + customId, customName,
                        buttonCount, axisCount, hatCount, vendorId, productId);
                    return [new("/maestro_created", id, ToJson(_definitions.DefineAndActivate(definition, overwrite)))];
                }

            case "/maestro_create_gamepad":
                // params: requestId, deviceId, displayName [, overwrite]
                if (m.Arguments.Length != 3 && m.Arguments.Length != 4)
                    throw new ArgumentException("Create gamepad arguments: requestId, deviceId, displayName [, overwrite]");
                {
                    string gamepadId = Text(m, 1);
                    string displayName = Text(m, 2);
                    bool overwrite = m.Arguments.Length == 4 && Boolean(m, 3);
                    var definition = new DeviceDefinition(gamepadId, "gamepad", "xbox-360-wireless", displayName);
                    return [new("/maestro_created", id, ToJson(_definitions.DefineAndActivate(definition, overwrite)))];
                }

            case "/maestro_activate":
                Count(m, 2); return [new("/maestro_created", id, ToJson(_definitions.Activate(Text(m, 1))))];
            case "/maestro_remove":
                Count(m, 2); _definitions.Deactivate(Text(m, 1)); break;
            case "/maestro_delete_all":
                Count(m, 3); 
                return [new("/maestro_all_deleted", id, ToJson(_definitions.DeleteAll(Integer(m,1), Integer(m,2))))];
            case "/maestro_rename":
                Count(m, 3); return [new("/maestro_renamed", id, ToJson(_definitions.Rename(Text(m, 1), Text(m, 2))))];
            case "/maestro_delete":
                Count(m, 2); return [new("/maestro_deleted", id, ToJson(_definitions.Delete(Text(m, 1))))];
            case "/maestro_capabilities":
                /// gets a profile info of the named device capability, or a list of all device capabilities if no name is provided
                Count(m, 2);
                {
                    var name = Text(m,1);
                    return [new("/maestro_capabilities_result", id, name, ToJson(backend.Capabilities(name)))];
                }
                
            case "/maestro_state":
                Count(m, 2);
                return [new("/maestro_state_result", id, Text(m, 1), ToJson(backend.State(Text(m, 1))))];
            case "/maestro_reset":
                Count(m, 2); backend.Reset(Text(m, 1)); break;
            default: throw new ArgumentException("Unknown command: " + m.Address);
        }
        return [new("/maestro_ok", id, m.Address)];
    }

    // Control values always come first; selectors are carried in the OSC address.
    // No arguments means read. Replies use the same address and normalized value.
    private IReadOnlyList<OscMessage> HandleControl(OscMessage m)
    {
        try
        {
            string[] parts = m.Address.Split('/');
            if (parts.Length != 4 || parts[2].Length == 0 || parts[3].Length == 0 || m.Arguments.Length > 1)
                throw new ArgumentException("Control address must be /maestro_KIND/device/control with zero arguments to read or one value to write");
            string id = BackendValidation.DeviceId(parts[2]);
            string control = parts[3]; bool write = m.Arguments.Length == 1;
            bool isGamepad = backend.Capabilities(id).Type.Equals("gamepad", StringComparison.OrdinalIgnoreCase);
            object value;
            switch (parts[1])
            {
                case "maestro_axis":
                    if (isGamepad && TryMapGamepadAxisRole(control, out string role))
                    {
                        if (write) backend.GamepadAxis(id, role, Number(m, 0));
                        value = backend.ReadGamepadAxis(id, role);
                    }
                    else
                    {
                        if (write) backend.Axis(id, control, Number(m, 0));
                        value = backend.ReadAxis(id, control);
                    }
                    break;
                case "maestro_gamepad_axis":
                    if (write) backend.GamepadAxis(id, control, Number(m, 0));
                    value = backend.ReadGamepadAxis(id, control); break;
                case "maestro_button":
                    if (isGamepad && TryMapGamepadDpadDirection(control, out int direction))
                    {
                        if (write) backend.Hat(id, 1, ButtonValue(m.Arguments[0]) ? direction : 0);
                        value = backend.ReadHat(id, 1) == direction ? 1 : 0;
                        break;
                    }
                    object selector = ResolveButtonSelector(control, isGamepad);
                    if (write) backend.Button(id, selector, ButtonValue(m.Arguments[0]));
                    value = backend.ReadButton(id, selector); break;
                case "maestro_hat":
                    int index = ResolveHatIndex(control, isGamepad);
                    if (write)
                    {
                        float position = Number(m, 0);
                        if (position < 0 || position > 8 || position != MathF.Truncate(position))
                            throw new ArgumentException("Hat position must be an integer from 0 to 8");
                        backend.Hat(id, index, (int)position);
                    }
                    value = backend.ReadHat(id, index); break;
                default: throw new ArgumentException("Unknown control address");
            }
            return [new OscMessage(m.Address, value)];
        }
        catch (Exception ex)
        {
            return [Error(-1, m.Address, ex is KeyNotFoundException ? "not_found" : ex is ArgumentException ? "bad_arguments" : "backend_error",
                ex.Message.Length > 1500 ? ex.Message[..1500] : ex.Message)];
        }
    }
    private static bool TryMapGamepadAxisRole(string control, out string role)
    {
        role = control.ToUpperInvariant() switch
        {
            "LX" or "X" => "LX",
            "LY" or "Y" => "LY",
            "RX" => "RX",
            "RY" => "RY",
            "LT" or "Z" => "LT",
            "RT" or "RZ" => "RT",
            _ => ""
        };
        return role.Length != 0;
    }
    private static object ResolveButtonSelector(string control, bool isGamepad)
    {
        if (int.TryParse(control, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out int number)) return number;
        if (!isGamepad) return control;
        return control.ToUpperInvariant() switch
        {
            "LB" or "L1" => 5,
            "RB" or "R1" => 6,
            "BACK" or "SELECT" => 7,
            "START" => 8,
            "GUIDE" or "XBOX" => 9,
            "LS" or "L3" => 10,
            "RS" or "R3" => 11,
            _ => control
        };
    }
    private static bool TryMapGamepadDpadDirection(string control, out int direction)
    {
        direction = control.ToUpperInvariant() switch
        {
            "DPAD_UP" or "DUP" or "UP" => 1,
            "DPAD_RIGHT" or "DRIGHT" or "RIGHT" => 3,
            "DPAD_DOWN" or "DDOWN" or "DOWN" => 5,
            "DPAD_LEFT" or "DLEFT" or "LEFT" => 7,
            _ => 0
        };
        return direction != 0;
    }
    private static int ResolveHatIndex(string control, bool isGamepad)
    {
        if (int.TryParse(control, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out int index)) return index;
        if (isGamepad && (control.Equals("hat", StringComparison.OrdinalIgnoreCase) ||
            control.Equals("dpad", StringComparison.OrdinalIgnoreCase))) return 1;
        throw new ArgumentException("Hat selector must be an integer index");
    }
    private static bool ButtonValue(object? arg) => arg switch
    {
        bool b => b,
        int n => n != 0,
        long n => n != 0,
        float f when float.IsFinite(f) => f != 0,
        double d when double.IsFinite(d) => d != 0,
        _ => throw new ArgumentException("Button value must be finite numeric (0=released, nonzero=pressed) or OSC boolean")
    };

    private static IEnumerable<OscMessage> Page(OscMessage m, int id, IReadOnlyList<object> rows, string address)
    {
        if (m.Arguments.Length != 1 && m.Arguments.Length != 3)
            throw new ArgumentException("List arguments: requestId [, offset, limit]");
        int offset = m.Arguments.Length == 3 ? Integer(m, 1) : 0;
        int limit = m.Arguments.Length == 3 ? Integer(m, 2) : 100;
        if (offset < 0 || limit is < 1 or > 200) throw new ArgumentException("Offset must be >=0; limit must be 1..200");
        int take = Math.Min(limit, Math.Max(0, rows.Count - offset));
        var replies = new List<OscMessage>();
        for (int i = 0; i < take; i++) replies.Add(new(address, id, offset + i, ToJson(rows[offset + i])));
        replies.Add(new("/maestro_done", id, m.Address, rows.Count, offset, take));
        return replies;
    }
    private static void Count(OscMessage m, int count)
    {
        if (m.Arguments.Length != count) throw new ArgumentException($"{m.Address} expects {count} arguments including requestId");
    }
    private static string Text(OscMessage m, int i) => m.Arguments[i] is string s && s.Length is > 0 and <= 256
        ? s : throw new ArgumentException($"Argument {i} must be a nonempty string of at most 256 characters");
    private static int Integer(OscMessage m, int i) => m.Arguments[i] is int n ? n : throw new ArgumentException($"Argument {i} must be OSC int32");
    private static float Number(OscMessage m, int i) => m.Arguments[i] switch
    {
        float f when float.IsFinite(f) => f,
        double d when double.IsFinite(d) && d >= -float.MaxValue && d <= float.MaxValue => (float)d,
        int n => n,
        _ => throw new ArgumentException($"Argument {i} must be a finite OSC float, double or int32")
    };
    private static bool Boolean(OscMessage m, int i) => m.Arguments[i] switch
    {
        bool b => b,
        int n => n != 0,
        long n => n != 0,
        float f when float.IsFinite(f) => f != 0,
        double d when double.IsFinite(d) => d != 0,
        _ => throw new ArgumentException($"Argument {i} must be OSC boolean or numeric")
    };

    public static OscMessage Error(int id, string address, string code, string message) => new("/maestro_error", id, address.Length > 512 ? address[..512] : address, code, message.Length > 1500 ? message[..1500] : message);

    private static string FormatArguments(object?[] args) => string.Join(", ", args.Select(a => a switch
    {
        null => "null",
        string s => '"' + s + '"',
        _ => Convert.ToString(a, System.Globalization.CultureInfo.InvariantCulture) ?? a.ToString() ?? ""
    }));
}
