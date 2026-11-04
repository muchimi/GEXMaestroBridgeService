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

using HIDMaestro;

namespace GEXMaestroBridgeService;

public sealed class HidMaestroBackend : IMaestroBackend
{
    private sealed class Device(string id, HMController controller, HMProfile profile, string kind = "profile", string? usedProfileName = null)
    {
        public string Id = id;
        public HMController Controller = controller;
        public HMProfile Profile = profile;
        public string Kind = kind;
        public string? UsedProfileName = usedProfileName;
        public HashSet<HMAxis> Axes = new(profile.AvailableAxes);
        public HMGamepadState State = new() { Axes = new(), Hat = HMHat.None };
        public int HatDirection = 0;
        public CustomJoystick? Custom;
    }
    private readonly HMContext _context = new();
    private readonly Dictionary<string, Device> _devices = new(StringComparer.Ordinal);

    public void Initialize(bool installDriver)
    {
        _context.LoadDefaultProfiles();
        if (installDriver) _context.InstallDriver();
    }
    private static bool LogicalHat(HMProfile p) => p.HasHat || p.Type.Equals("gamepad", StringComparison.OrdinalIgnoreCase) || p.HasExtendedInput;
    private static ProfileInfo Describe(HMProfile p) => new(p.Id, p.Name, p.VendorId, p.ProductId,
        p.Type, p.IsDeployable, p.ButtonCount, p.AvailableAxes.Select(a => a.ToString()).ToArray(),
        p.HasHat, p.HatLogicalMin, p.HatLogicalMax, p.ButtonMap, LogicalHat(p) ? 1 : 0, 32);
    private static DeviceInfo Describe(Device d)
    {
        var profile = Describe(d.Profile);
        bool fromMaestroProfile = d.Custom is null;
        return new(d.Id, d.Profile.Id, d.Profile.Name, d.Controller.TransportIndex,
            true, fromMaestroProfile ? profile.Type : "custom", fromMaestroProfile ? profile : null);
    }
    private Device Get(string id) => _devices.TryGetValue(id, out var d) ? d : throw new KeyNotFoundException("Unknown service device ID: " + id);
    public IReadOnlyList<ProfileInfo> Profiles() => _context.AllProfiles.Select(Describe).ToArray();
    public IReadOnlyList<DeviceInfo> Devices() => _devices.Values.Select(Describe).ToArray();
    public ProfileInfo Capabilities(string id)
    {
        var d = Get(id);
        if (d.Custom is not null)
            return d.Custom.Capabilities(id, d.Profile.Name) with { Kind = d.Kind, UsedProfileName = null };
        return Describe(d.Profile) with { Kind = d.Kind, UsedProfileName = d.UsedProfileName };
    }
    public StateInfo State(string id)
    {
        var d = Get(id);
        if (d.Custom is not null) return d.Custom.State();
        return new(d.State.Axes!.ToDictionary(x => x.Key.ToString(), x => x.Value), (uint)d.State.Buttons, d.HatDirection);
    }
    public DeviceInfo Create(string id, string profileId, string? name = null)
    {
        BackendValidation.DeviceId(id);
        if (_devices.TryGetValue(id, out var existing))
            return existing.Profile.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase) && (name is null || existing.Profile.Name == name) ? Describe(existing)
                : throw new InvalidOperationException("Device ID already has a different profile");
        var profile = _context.GetProfile(profileId) ?? throw new KeyNotFoundException("Unknown profile: " + profileId);
        if (!profile.IsDeployable) throw new ArgumentException("Profile has no deployable descriptor");
        if (profile.Type.Equals("keyboard", StringComparison.OrdinalIgnoreCase) || profile.Type.Equals("mouse", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("This bridge supports controller profiles, not keyboard/mouse reports");
        if (name is not null)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128) throw new ArgumentException("Name must have 1..128 characters");
            profile = new HMProfileBuilder().FromProfile(profile).Name(name).ProductString(name).DeviceDescription(name).Build();
        }
        // Namespace keys to avoid colliding with another HIDMaestro consumer's identities.
        var controller = _context.CreateController(profile, "MaestroOscService:" + id);
        try
        {
            string usedProfileName = _context.GetProfile(profile.Id)?.Name ?? profile.Name;
            var d = new Device(id, controller, profile, "profile", usedProfileName);
            controller.SubmitState(d.State);
            _devices.Add(id, d);
            return Describe(d);
        }
        catch { controller.Dispose(); throw; }
    }
    public DeviceInfo CreateCustom(string id, string name, int buttons, int axes, int hats, int vendorId, int productId)
    {
        BackendValidation.DeviceId(id);
        if (name.Length is < 1 or > 128) throw new ArgumentException("Custom name must have 1..128 characters");
        var custom = new CustomJoystick(buttons, axes, hats);
        if (_devices.TryGetValue(id, out var existing))
        {
            if (existing.Custom is not null && existing.Custom.ButtonCount == buttons && existing.Custom.AxisCount == axes &&
                existing.Custom.HatCount == hats && existing.Profile.Name == name) return Describe(existing);
            throw new InvalidOperationException("Device ID has a different definition");
        }
        var profile = new HMProfileBuilder().Id("gex-" + id).Name(name).Vendor("GEX")
            .Vid((ushort)vendorId).Pid((ushort)productId).ProductString(name).ManufacturerString("GEX")
            .DeviceDescription(name).Type("joystick").Connection("usb").Descriptor(custom.Descriptor()).InputReportSize(custom.ReportSize)
            .DeviceModel("none").XInput("{}").Build();
        var controller = _context.CreateController(profile, "GEX:" + id);
        try
        {
            controller.SubmitRawReport(custom.Report());
            var d = new Device(id, controller, profile, "custom") { Custom = custom };
            _devices.Add(id, d); return Describe(d);
        }
        catch { controller.Dispose(); throw; }
    }
    public DeviceInfo CreateGamepad(string id, string name)
    {
        var info = Create(id, "xbox-360-wireless", name);
        var d = Get(id);
        d.Kind = "gamepad";
        d.UsedProfileName = _context.GetProfile("xbox-360-wireless")?.Name ?? "Xbox 360 Wireless Controller";
        return info;
    }
    public void GamepadAxis(string id, string role, float value)
    {
        var d = Get(id); BackendValidation.AxisValue(value);
        if (d.Custom is not null) throw new ArgumentException("Use /maestro_axis for custom joysticks");
        HMAxis key = ResolveRole(d.Profile, role);
        var next = d.State; next.Axes = new(d.State.Axes!) { [key] = value };
        d.Controller.SubmitState(next); d.State = next;
    }
    private static HMAxis ResolveRole(HMProfile profile, string role)
    {
        string r = role.ToUpperInvariant();
        if (!new[] { "LX", "LY", "RX", "RY", "LT", "RT" }.Contains(r))
            throw new ArgumentException("Gamepad axis role must be LX, LY, RX, RY, LT or RT");
        var baseline = HMGamepadStateHelpers.StandardAxes(profile);
        var probe = HMGamepadStateHelpers.StandardAxes(profile,
            leftStickX: r == "LX" ? 0.731f : 0.5f, leftStickY: r == "LY" ? 0.731f : 0.5f,
            rightStickX: r == "RX" ? 0.731f : 0.5f, rightStickY: r == "RY" ? 0.731f : 0.5f,
            leftTrigger: r == "LT" ? 0.731f : 0f, rightTrigger: r == "RT" ? 0.731f : 0f);
        var keys = probe.Where(p => !baseline.TryGetValue(p.Key, out var v) || v != p.Value).Select(p => p.Key).ToArray();
        if (keys.Length != 1) throw new ArgumentException("Profile does not expose a unique gamepad axis for that role");
        return keys[0];
    }
    public void Remove(string id)
    {
        var d = Get(id);
        d.Controller.Dispose();
        _devices.Remove(id);
    }
    public void Button(string id, object selector, bool pressed)
    {
        var d = Get(id);
        if (d.Custom is not null)
        {
            if (selector is not int number) throw new ArgumentException("Custom joystick buttons require int32 indices");
            d.Custom.Button(number, pressed, raw => d.Controller.SubmitRawReport(raw)); return;
        }
        uint mask = ButtonMask(selector);
        var next = d.State;
        uint buttons = (uint)next.Buttons;
        next.Buttons = (HMButton)(pressed ? buttons | mask : buttons & ~mask);
        d.Controller.SubmitState(next);
        d.State = next;
    }
    private static uint ButtonMask(object selector)
    {
        uint mask;
        if (selector is string name)
        {
            // Reject numeric strings and combined flags. Numeric selectors must be OSC int32.
            if (!Enum.GetNames<HMButton>().Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) ||
                !Enum.TryParse<HMButton>(name, true, out var button))
                throw new ArgumentException("Unknown HMButton name");
            mask = (uint)button;
            if (mask == 0 || (mask & (mask - 1)) != 0) throw new ArgumentException("Select exactly one button");
        }
        else mask = 1u << BackendValidation.NumericButton(selector);
        return mask;
    }
    public void Axis(string id, string axis, float value)
    {
        var d = Get(id); BackendValidation.AxisValue(value);
        if (d.Custom is not null) { d.Custom.Axis(axis, value, raw => d.Controller.SubmitRawReport(raw)); return; }
        if (!Enum.TryParse<HMAxis>(axis, true, out var key) || !d.Axes.Contains(key))
            throw new ArgumentException("Axis unavailable; use /maestro_capabilities to list axes");
        // Copy before submitting: on SDK failure the retained state stays unchanged.
        var next = d.State;
        next.Axes = new(d.State.Axes!) { [key] = value };
        d.Controller.SubmitState(next);
        d.State = next;
    }
    public void Hat(string id, int index, int direction)
    {
        var d = Get(id);
        if (d.Custom is not null) { d.Custom.Hat(index, direction, raw => d.Controller.SubmitRawReport(raw)); return; }
        BackendValidation.HatValue(index, direction);
        if (!LogicalHat(d.Profile)) throw new ArgumentException("Profile has no hat/D-pad input");
        var next = d.State;
        next.HatDegrees = null; next.HatHundredths = null; next.HatRaw = null;
        next.Hat = (HMHat)direction;
        d.Controller.SubmitState(next); d.State = next; d.HatDirection = direction;
    }
    public void Reset(string id)
    {
        var d = Get(id);
        if (d.Custom is not null)
        {
            var neutral = new CustomJoystick(d.Custom.ButtonCount, d.Custom.AxisCount, d.Custom.HatCount);
            d.Controller.SubmitRawReport(neutral.Report()); d.Custom = neutral; return;
        }
        var next = new HMGamepadState { Axes = new(), Hat = HMHat.None };
        d.Controller.SubmitState(next); d.State = next; d.HatDirection = 0;
    }
    public float ReadAxis(string id, string axis)
    {
        var d = Get(id);
        if (d.Custom is not null) return d.Custom.ReadAxis(axis);
        if (!Enum.TryParse<HMAxis>(axis, true, out var key) || !d.Axes.Contains(key)) throw new ArgumentException("Axis unavailable");
        if (d.State.Axes!.TryGetValue(key, out float value)) return value;
        // Stick/trigger roles determine defaults even for unsigned HID stick fields.
        var baseline = HMGamepadStateHelpers.StandardAxes(d.Profile);
        if (baseline.TryGetValue(key, out float neutral)) return neutral;
        return HidAxisDefaults.Neutral(d.Profile.GetDescriptorBytes()!, (ushort)key);
    }
    public int ReadButton(string id, object selector)
    {
        var d = Get(id);
        if (d.Custom is not null) return selector is int number ? d.Custom.ReadButton(number) : throw new ArgumentException("Custom buttons require int32 indices");
        return ((uint)d.State.Buttons & ButtonMask(selector)) != 0 ? 1 : 0;
    }
    public int ReadHat(string id, int index)
    {
        var d = Get(id); if (d.Custom is not null) return d.Custom.ReadHat(index);
        BackendValidation.HatValue(index, 0);
        if (!LogicalHat(d.Profile)) throw new ArgumentException("Profile has no hat/D-pad input");
        return d.HatDirection;
    }
    public float ReadGamepadAxis(string id, string role)
    {
        var d = Get(id); if (d.Custom is not null) throw new ArgumentException("Use /maestro_axis for custom joysticks");
        HMAxis key = ResolveRole(d.Profile, role);
        if (d.State.Axes!.TryGetValue(key, out float value)) return value;
        return role.Equals("LT", StringComparison.OrdinalIgnoreCase) || role.Equals("RT", StringComparison.OrdinalIgnoreCase) ? 0f : 0.5f;
    }
    public void Dispose() => _context.Dispose();
}
