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

using System.Buffers.Binary;

namespace GEXMaestroBridgeService;

// Descriptor and report layout are defined together. No HMGamepadState/32-bit mask is used.
public sealed class CustomJoystick
{
    public static readonly string[] AxisNames = ["X", "Y", "Z", "Rx", "Ry", "Rz", "Slider", "Dial"];
    public int ButtonCount { get; }
    public int AxisCount { get; }
    public int HatCount { get; }
    private readonly bool[] _buttons;
    private readonly float[] _axes;
    private readonly int[] _hats;
    public int ReportSize => (ButtonCount + 7) / 8 + AxisCount * 2 + HatCount;

    public CustomJoystick(int buttons, int axes, int hats)
    {
        Validate(buttons, axes, hats);
        ButtonCount = buttons; AxisCount = axes; HatCount = hats;
        _buttons = new bool[buttons]; _axes = Enumerable.Repeat(0.5f, axes).ToArray();
        _hats = Enumerable.Repeat(0, hats).ToArray();
    }
    public static void Validate(int buttons, int axes, int hats)
    {
        if (buttons is < 0 or > 128 || axes is < 0 or > 8 || hats is < 0 or > 4 || buttons + axes + hats == 0)
            throw new ArgumentException("Custom joystick requires 0..128 buttons, 0..8 axes, 0..4 hats and at least one control");
    }
    public byte[] Descriptor()
    {
        var d = new List<byte> { 0x05,0x01, 0x09,0x04, 0xA1,0x01 }; // Desktop / Joystick / Application
        if (ButtonCount > 0)
        {
            d.AddRange([0x05,0x09, 0x19,0x01, 0x29,(byte)ButtonCount,
                0x15,0x00, 0x25,0x01, 0x75,0x01, 0x95,(byte)ButtonCount, 0x81,0x02]);
            int pad = (8 - ButtonCount % 8) % 8;
            if (pad > 0) d.AddRange([0x75,0x01, 0x95,(byte)pad, 0x81,0x03]);
        }
        if (AxisCount > 0)
        {
            d.AddRange([0x05,0x01]);
            for (int i = 0; i < AxisCount; i++) d.AddRange([0x09,(byte)(0x30 + i)]);
            // Signed 16-bit axes, logical -32767..32767. Physical bounds follow logical bounds.
            d.AddRange([0x16,0x01,0x80, 0x26,0xFF,0x7F, 0x36,0x01,0x80, 0x46,0xFF,0x7F,
                0x75,0x10, 0x95,(byte)AxisCount, 0x81,0x02]);
        }
        if (HatCount > 0)
        {
            d.AddRange([0x05,0x01]);
            for (int i = 0; i < HatCount; i++) d.AddRange([0x09,0x39]);
            // Eight directions; 255 is outside LogicalMax and is therefore the null state.
            d.AddRange([0x15,0x00, 0x25,0x07, 0x35,0x00, 0x46,0x3B,0x01, 0x65,0x14,
                0x75,0x08, 0x95,(byte)HatCount, 0x81,0x42, 0x65,0x00]);
        }
        d.Add(0xC0);
        return d.ToArray();
    }
    public byte[] Report()
    {
        byte[] report = new byte[ReportSize];
        for (int i = 0; i < ButtonCount; i++) if (_buttons[i]) report[i / 8] |= (byte)(1 << (i % 8));
        int offset = (ButtonCount + 7) / 8;
        foreach (float axis in _axes)
        {
            short raw = (short)Math.Round(axis * 65534.0 - 32767.0, MidpointRounding.AwayFromZero);
            BinaryPrimitives.WriteInt16LittleEndian(report.AsSpan(offset), raw); offset += 2;
        }
        foreach (int hat in _hats) report[offset++] = hat == 0 ? (byte)255 : (byte)(hat - 1);
        return report;
    }
    // Retain old values if the SDK rejects submission.
    public void Button(int number, bool pressed, Action<byte[]> submit)
    {
        if (number < 1 || number > ButtonCount) throw new ArgumentException($"Button must be 1..{ButtonCount}");
        int i = number - 1; bool old = _buttons[i]; _buttons[i] = pressed;
        try { submit(Report()); } catch { _buttons[i] = old; throw; }
    }
    public void Axis(string name, float value, Action<byte[]> submit)
    {
        BackendValidation.AxisValue(value);
        int i = Array.FindIndex(AxisNames, n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i >= AxisCount) throw new ArgumentException("Axis is not defined on this custom joystick");
        float old = _axes[i]; _axes[i] = value;
        try { submit(Report()); } catch { _axes[i] = old; throw; }
    }
    public void Hat(int number, int direction, Action<byte[]> submit)
    {
        if (number < 1 || number > HatCount || direction is < 0 or > 8)
            throw new ArgumentException($"Hat must be 1..{HatCount}; position 0..8");
        int i = number - 1; int old = _hats[i]; _hats[i] = direction;
        try { submit(Report()); } catch { _hats[i] = old; throw; }
    }
    public float ReadAxis(string name)
    {
        int i = Array.FindIndex(AxisNames, n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i >= AxisCount) throw new ArgumentException("Axis unavailable"); return _axes[i];
    }
    public int ReadButton(int number)
    {
        if (number < 1 || number > ButtonCount) throw new ArgumentException("Button index outside configured range");
        return _buttons[number - 1] ? 1 : 0;
    }
    public int ReadHat(int number)
    {
        if (number < 1 || number > HatCount) throw new ArgumentException("Hat index outside configured range");
        return _hats[number - 1];
    }
    public StateInfo State() => new(Enumerable.Range(0, AxisCount).ToDictionary(i => AxisNames[i], i => _axes[i]),
        Enumerable.Range(0, Math.Min(32, ButtonCount)).Aggregate(0L, (mask, i) => _buttons[i] ? mask | (1L << i) : mask),
        HatCount > 0 ? _hats[0] : 0,
        Enumerable.Range(0, ButtonCount).Where(i => _buttons[i]).Select(i => i + 1).ToArray(), (int[])_hats.Clone());
    public ProfileInfo Capabilities(string id, string name) => new("custom-" + id, name, 0xFEED, 0x8010,
        "joystick", true, ButtonCount, AxisNames.Take(AxisCount).ToArray(), HatCount > 0,
        HatCount > 0 ? 0 : null, HatCount > 0 ? 7 : null, null, HatCount, ButtonCount);
}
