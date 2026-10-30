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

using System.Management;

namespace GEXMaestroBridgeService;

public sealed record ProfileInfo(string Id, string Name, int VendorId, int ProductId,
    string Type, bool Deployable, int ButtonCount, string[] Axes, bool HasHat,
    int? HatLogicalMin, int? HatLogicalMax, int[]? ButtonMap, int HatCount = 1, int AddressableButtons = 32);
public sealed record DeviceInfo(string Id, string ProfileId, string Name, int TransportIndex,
    bool Controllable = true);
public sealed record StateInfo(Dictionary<string, float> Axes, long Buttons, int Hat, int[]? PressedButtons = null, int[]? Hats = null);

// All methods are called by a single serialized command loop.
public interface IMaestroBackend : IDisposable
{
    void Initialize(bool installDriver);
    IReadOnlyList<ProfileInfo> Profiles();
    IReadOnlyList<DeviceInfo> Devices();
    DeviceInfo Create(string id, string profileId, string? name = null);
    DeviceInfo CreateCustom(string id, string name, int buttons, int axes, int hats);
    void GamepadAxis(string id, string role, float value);
    void Remove(string id);
    ProfileInfo Capabilities(string id);
    StateInfo State(string id);
    void Button(string id, object selector, bool pressed);
    void Axis(string id, string axis, float value);
    void Hat(string id, int index, int direction);
    void Reset(string id);
    float ReadAxis(string id, string axis);
    int ReadButton(string id, object selector);
    int ReadHat(string id, int index);
    float ReadGamepadAxis(string id, string role);
}

public static class BackendValidation
{
    public static string DeviceId(string id)
    {
        if (id.Length is < 1 or > 64 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-'))
            throw new ArgumentException("Device ID must contain 1-64 ASCII letters, digits, underscores or hyphens");
        return id;
    }
    public static int NumericButton(object selector)
    {
        if (selector is not int n || n < 1 || n > 32)
            throw new ArgumentException("Button must be an OSC int32 from 1 to 32 or a supported HMButton name");
        return n - 1;
    }
    public static void AxisValue(float value)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1)
            throw new ArgumentException("Axis value must be finite and between 0 and 1");
    }
    public static void HatValue(int index, int direction)
    {
        if (index != 1) throw new ArgumentException("The high-level HIDMaestro API exposes one hat: index 1");
        if (direction is < 0 or > 8) throw new ArgumentException("Hat position must be 0 (center) or 1..8 clockwise from North");
    }
}

public static class SystemDevices
{
    // Read-only inventory. Windows PnP IDs are not service device IDs and cannot be claimed.
    public static IReadOnlyList<object> Enumerate()
    {
        using var search = new ManagementObjectSearcher(
            "SELECT Name,PNPDeviceID,Manufacturer,Service,Status FROM Win32_PnPEntity " +
            "WHERE Present = TRUE AND (PNPClass = 'HIDClass' OR Service LIKE 'HIDMaestro%')");
        using var rows = search.Get();
        var result = new List<object>();
        foreach (ManagementObject row in rows)
        {
            using (row)
                result.Add(new { name = row["Name"]?.ToString(), pnpDeviceId = row["PNPDeviceID"]?.ToString(),
                    manufacturer = row["Manufacturer"]?.ToString(), service = row["Service"]?.ToString(),
                    status = row["Status"]?.ToString(), controllable = false });
        }
        return result;
    }
}

// Used only for explicit --mock / Mock=true and protocol tests. Does not create hardware.
public sealed class MockBackend : IMaestroBackend
{
    private sealed class Device(string id, ProfileInfo profile)
    {
        public DeviceInfo Info = new(id, profile.Id, profile.Name, 0);
        public ProfileInfo Profile = profile;
        public Dictionary<string, float> Axes = new(StringComparer.OrdinalIgnoreCase);
        public uint Buttons;
        public int Hat = 0;
        public CustomJoystick? Custom;
    }
    private readonly Dictionary<string, Device> _devices = new(StringComparer.Ordinal);
    private readonly ProfileInfo _profile = new("mock-gamepad", "Mock gamepad (no hardware)", 0, 0,
        "gamepad", true, 32, ["X", "Y", "Rx", "Ry", "Z", "Rz"], true, 0, 7, null);
    public void Initialize(bool installDriver) { }
    public IReadOnlyList<ProfileInfo> Profiles() => [_profile];
    public IReadOnlyList<DeviceInfo> Devices() => _devices.Values.Select(d => d.Info).ToArray();
    private Device Get(string id) => _devices.TryGetValue(id, out var d) ? d : throw new KeyNotFoundException("Unknown service device ID: " + id);
    public DeviceInfo Create(string id, string profileId, string? name = null)
    {
        BackendValidation.DeviceId(id);
        if (_devices.TryGetValue(id, out var existing))
            return existing.Profile.Id == profileId && existing.Profile.Name == (name ?? _profile.Name) ? existing.Info : throw new InvalidOperationException("Device ID already has a different profile");
        if (profileId != _profile.Id) throw new KeyNotFoundException("Unknown mock profile");
        var d = new Device(id, _profile with { Name = name ?? _profile.Name }); _devices.Add(id, d); return d.Info;
    }
    public DeviceInfo CreateCustom(string id, string name, int buttons, int axes, int hats)
    {
        BackendValidation.DeviceId(id);
        var custom = new CustomJoystick(buttons, axes, hats);
        if (_devices.TryGetValue(id, out var prior))
        {
            if (prior.Custom is not null && prior.Custom.ButtonCount == buttons && prior.Custom.AxisCount == axes && prior.Custom.HatCount == hats && prior.Profile.Name == name)
                return prior.Info;
            throw new InvalidOperationException("Device ID has a different definition");
        }
        var profile = custom.Capabilities(id, name);
        var d = new Device(id, profile) { Custom = custom }; _devices.Add(id, d); return d.Info;
    }
    public void GamepadAxis(string id, string role, float value)
    {
        if (Get(id).Custom is not null) throw new ArgumentException("Use /maestro_axis for custom joysticks");
        string axis = role.ToUpperInvariant() switch { "LX" => "X", "LY" => "Y", "RX" => "Rx", "RY" => "Ry", "LT" => "Z", "RT" => "Rz", _ => throw new ArgumentException("Invalid role") };
        Axis(id, axis, value);
    }
    public void Remove(string id) { Get(id); _devices.Remove(id); }
    public ProfileInfo Capabilities(string id) { var d = Get(id); return d.Custom?.Capabilities(id, d.Profile.Name) ?? d.Profile; }
    public StateInfo State(string id) { var d = Get(id); return d.Custom?.State() ?? new(new(d.Axes), d.Buttons, d.Hat); }
    public void Button(string id, object selector, bool pressed)
    {
        var d = Get(id);
        if (d.Custom is not null)
        { if (selector is not int number) throw new ArgumentException("Custom buttons need int32"); d.Custom.Button(number, pressed, _ => { }); return; }
        int bit = selector is string name ? name.ToLowerInvariant() switch
        { "a" => 0, "b" => 1, "x" => 2, "y" => 3, _ => throw new ArgumentException("Mock supports named buttons A/B/X/Y") }
            : BackendValidation.NumericButton(selector);
        uint mask = 1u << bit; d.Buttons = pressed ? d.Buttons | mask : d.Buttons & ~mask;
    }
    public void Axis(string id, string axis, float value)
    {
        var d = Get(id); BackendValidation.AxisValue(value);
        if (d.Custom is not null) { d.Custom.Axis(axis, value, _ => { }); return; }
        string? actual = d.Profile.Axes.FirstOrDefault(a => a.Equals(axis, StringComparison.OrdinalIgnoreCase));
        if (actual is null) throw new ArgumentException("Axis unavailable in profile");
        d.Axes[actual] = value;
    }
    public void Hat(string id, int index, int direction) { var d = Get(id); if (d.Custom is not null) { d.Custom.Hat(index, direction, _ => { }); return; } BackendValidation.HatValue(index, direction); d.Hat = direction; }
    public void Reset(string id) { var d = Get(id); if (d.Custom is not null) { d.Custom = new(d.Custom.ButtonCount, d.Custom.AxisCount, d.Custom.HatCount); return; } d.Axes.Clear(); d.Buttons = 0; d.Hat = 0; }
    public float ReadAxis(string id, string axis)
    {
        var d = Get(id);
        if (d.Custom is not null) return d.Custom.ReadAxis(axis);
        string? actual = d.Profile.Axes.FirstOrDefault(a => a.Equals(axis, StringComparison.OrdinalIgnoreCase));
        if (actual is null) throw new ArgumentException("Axis unavailable");
        return d.Axes.TryGetValue(actual, out var value) ? value : actual is "Z" or "Rz" ? 0f : 0.5f;
    }
    public int ReadButton(string id, object selector)
    {
        var d = Get(id);
        if (d.Custom is not null) return selector is int number ? d.Custom.ReadButton(number) : throw new ArgumentException("Custom buttons need int32 indices");
        int bit = selector is string name ? name.ToLowerInvariant() switch
        { "a" => 0, "b" => 1, "x" => 2, "y" => 3, _ => throw new ArgumentException("Mock supports named buttons A/B/X/Y") }
            : BackendValidation.NumericButton(selector);
        return (d.Buttons & (1u << bit)) != 0 ? 1 : 0;
    }
    public int ReadHat(string id, int index)
    {
        var d = Get(id); if (d.Custom is not null) return d.Custom.ReadHat(index);
        BackendValidation.HatValue(index, 0); return d.Hat;
    }
    public float ReadGamepadAxis(string id, string role)
    {
        if (Get(id).Custom is not null) throw new ArgumentException("Use /maestro_axis for custom joysticks");
        string axis = role.ToUpperInvariant() switch { "LX" => "X", "LY" => "Y", "RX" => "Rx", "RY" => "Ry", "LT" => "Z", "RT" => "Rz", _ => throw new ArgumentException("Invalid role") };
        return ReadAxis(id, axis);
    }
    public void Dispose() => _devices.Clear();
}
