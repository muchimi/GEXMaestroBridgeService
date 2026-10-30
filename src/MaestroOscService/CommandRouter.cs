/*
 
 Gremlin Ex and the Gremlin Ex Bridge Service for HIDMaestro is (C) EMCS 2026 under a GNU General Public License.


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
                return Page(m, id, backend.Devices().Cast<object>().ToArray(), "/maestro_device");
            case "/maestro_definitions":
                return Page(m, id, _definitions.Definitions().Cast<object>().ToArray(), "/maestro_definition");
            case "/maestro_system_devices":
                return Page(m, id, systemInventory(), "/maestro_system_device");
            case "/maestro_create":
                if (m.Arguments.Length != 3 && m.Arguments.Length != 4) throw new ArgumentException("Create arguments: requestId, deviceId, profileId [, displayName]");
                string deviceId = Text(m, 1), profileId = Text(m, 2);
                return [new("/maestro_created", id, ToJson(_definitions.DefineAndActivate(
                    new(deviceId, "profile", profileId, m.Arguments.Length == 4 ? Text(m, 3) : backend.Profiles().FirstOrDefault(p => p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase))?.Name ?? profileId))))];
            case "/maestro_create_custom":
                Count(m, 6);
                return [new("/maestro_created", id, ToJson(_definitions.DefineAndActivate(
                    new(Text(m, 1), "custom", "custom-" + Text(m, 1), Text(m, 2), Integer(m, 3), Integer(m, 4), Integer(m, 5)))))];
            case "/maestro_activate":
                Count(m, 2); return [new("/maestro_created", id, ToJson(_definitions.Activate(Text(m, 1))))];
            case "/maestro_remove":
                Count(m, 2); _definitions.Deactivate(Text(m, 1)); break;
            case "/maestro_rename":
                Count(m, 3); return [new("/maestro_renamed", id, ToJson(_definitions.Rename(Text(m, 1), Text(m, 2))))];
            case "/maestro_delete":
                Count(m, 2); _definitions.Delete(Text(m, 1)); break;
            case "/maestro_capabilities":
                Count(m, 2);
                return [new("/maestro_capabilities_result", id, Text(m, 1), ToJson(backend.Capabilities(Text(m, 1))))];
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
            object value;
            switch (parts[1])
            {
                case "maestro_axis":
                    if (write) backend.Axis(id, control, Number(m, 0));
                    value = backend.ReadAxis(id, control); break;
                case "maestro_gamepad_axis":
                    if (write) backend.GamepadAxis(id, control, Number(m, 0));
                    value = backend.ReadGamepadAxis(id, control); break;
                case "maestro_button":
                    object selector = int.TryParse(control, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out int number) ? number : control;
                    if (write) backend.Button(id, selector, ButtonValue(m.Arguments[0]));
                    value = backend.ReadButton(id, selector); break;
                case "maestro_hat":
                    if (!int.TryParse(control, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out int index)) throw new ArgumentException("Hat selector must be an integer index");
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
    public static OscMessage Error(int id, string address, string code, string message) => new("/maestro_error", id, address.Length > 512 ? address[..512] : address, code, message.Length > 1500 ? message[..1500] : message);
}
