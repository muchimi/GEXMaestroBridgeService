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

public static class SelfTests
{
    public static void Run()
    {
        int checks = PortSettingsTests.Run();
        void Check(bool pass, string name) { if (!pass) throw new Exception("FAIL: " + name); checks++; }
        void Reject(byte[] data, string name)
        {
            try { OscCodec.Decode(data); throw new Exception("FAIL: accepted " + name); }
            catch (FormatException) { checks++; }
        }
        byte[] golden = Convert.FromHexString("2F7800002C69667300000000000000013F00000061000000");
        byte[] encoded = OscCodec.Encode(new("/x", 1, 0.5f, "a"));
        Check(encoded.SequenceEqual(golden), "OSC independent golden vector");
        var decoded = OscCodec.Decode(golden).Single();
        Check(decoded.Address == "/x" && decoded.Arguments.SequenceEqual(new object[] { 1, 0.5f, "a" }), "decode golden vector");
        var extended = new OscMessage("/maestro_ping", 7, true, false, null, 123L, 0.25d, "pad");
        Check(OscCodec.Decode(OscCodec.Encode(extended)).Single().Arguments.SequenceEqual(extended.Arguments), "extended types");
        byte[] bundle = Bundle(encoded);
        Check(OscCodec.Decode(bundle).Single().Address == "/x", "immediate bundle");
        Reject(bundle.Concat(new byte[4]).ToArray(), "truncated bundle element");
        byte[] future = (byte[])bundle.Clone(); future[15] = 2; Reject(future, "unsupported timetag");
        Reject(encoded[..^1], "unaligned packet");
        Reject(Convert.FromHexString("2F7800012C000000"), "nonzero string padding");
        Reject(Convert.FromHexString("2F7800002C62000000000000"), "unsupported blob");
        Reject(Convert.FromHexString("2F7800002C690000"), "missing argument payload");
        byte[] nested = encoded; for (int i = 0; i < 10; i++) nested = Bundle(nested); Reject(nested, "nesting limit");
        using var backend = new MockBackend();
        var router = new CommandRouter(backend, 1, () => []);
        int id = 0;
        OscMessage Call(string address, params object?[] args) => router.Handle(new(address, new object?[] { ++id }.Concat(args).ToArray()), "client").Single();
        OscMessage Control(string address, params object?[] args) => router.Handle(new(address, args), "client").Single();
        bool Value(string address, object expected, params object?[] args)
        { var reply = Control(address, args); return reply.Address == address && Equals(reply.Arguments.Single(), expected); }
        Check(Call("/maestro_create", "pad", "mock-gamepad", "Named pad").Address == "/maestro_created", "named console create");
        Check(backend.Devices().Single().Name == "Named pad", "console name");
        Check(Call("/maestro_create", "pad", "mock-gamepad", "Named pad").Address == "/maestro_created", "idempotent create");
        Check(Call("/maestro_create", "pad2", "mock-gamepad").Address == "/maestro_error", "device limit");
        Check(Value("/maestro_button/pad/A", 1, -2.5f), "nonzero float button press");
        Check(Value("/maestro_button/pad/A", 1), "same-address button read");
        Check(Value("/maestro_button/pad/A", 0, 0f), "zero float releases");
        Check(Value("/maestro_axis/pad/X", 0.75f, 0.75f), "value-first axis");
        Check(Value("/maestro_hat/pad/1", 8, 8f), "hat accepts integral numeric position 8");
        Check(Value("/maestro_hat/pad/1", 8), "same-address hat read");
        Check(Value("/maestro_button/pad/32", 1, true), "boolean button press");
        Check(backend.State("pad").Buttons == 2147483648L && backend.State("pad").Axes["X"] == 0.75f && backend.State("pad").Hat == 8,
            "independent retained state");
        Check(Value("/maestro_gamepad_axis/pad/LT", 0.25f, 0.25f), "console trigger role");
        Check(Value("/maestro_gamepad_axis/pad/LT", 0.25f), "console trigger read");
        foreach (var message in new[] {
            new OscMessage("/maestro_axis/pad/X", float.NaN), new OscMessage("/maestro_axis/pad/X", 2f),
            new OscMessage("/maestro_axis/pad/Bogus", 0.5f), new OscMessage("/maestro_button/pad/33", 1),
            new OscMessage("/maestro_hat/pad/2", 1), new OscMessage("/maestro_hat/pad/1", 9),
            new OscMessage("/maestro_hat/pad/1", -1), new OscMessage("/maestro_hat/pad/1", 1.5f),
            new OscMessage("/maestro_button/pad/1", float.NaN), new OscMessage("/maestro_axis/pad/X", 0.5f, 9)
        }) Check(router.Handle(message, "client").Single().Address == "/maestro_error", "reject invalid control " + message.Address);
        Check(Call("/other_axis", "pad", "X", 0f).Address == "/maestro_error", "prefix enforcement");
        Check(Call("/maestro_reset").Address == "/maestro_error", "arity");
        var replay = new OscMessage("/maestro_reset", 10000, "pad");
        var first = router.Handle(replay, "client"); Control("/maestro_button/pad/1", 1);
        Check(router.Handle(replay, "client").SequenceEqual(first) && backend.ReadButton("pad", 1) == 1, "management replay does not reapply reset");
        Check(router.Handle(new("/maestro_remove", 10000, "pad"), "client").Single().Arguments[2]?.ToString() == "request_id_conflict", "request collision");
        Check(Call("/maestro_rename", "pad", "Renamed pad").Address == "/maestro_renamed", "rename console");
        Check(backend.Devices().Single().Name == "Renamed pad" && backend.ReadButton("pad", 1) == 0, "rename recreates neutral with same ID");
        Call("/maestro_delete", "pad");
        Check(backend.Devices().Count == 0, "delete active device");
        Check(Call("/maestro_create_gamepad", "xpad", "Xbox Wireless").Address == "/maestro_created", "gamepad create");
        Check(Value("/maestro_axis/xpad/X", 0.66f, 0.66f), "gamepad axis X write/read");
        Check(Value("/maestro_gamepad_axis/xpad/LX", 0.66f), "gamepad role LX reads mapped X value");
        Check(Value("/maestro_axis/xpad/LT", 0.4f, 0.4f), "gamepad axis LT write/read");
        Check(Value("/maestro_axis/xpad/Z", 0.4f), "gamepad axis Z reads mapped LT value");
        Check(Value("/maestro_button/xpad/LB", 1, 1), "gamepad shoulder alias write");
        Check(Value("/maestro_button/xpad/LB", 1), "gamepad shoulder alias read");
        Check(Value("/maestro_button/xpad/DPAD_UP", 1, 1), "gamepad dpad up as button write/read");
        Check(Value("/maestro_hat/xpad/dpad", 1), "gamepad dpad up mapped to hat north");
        Check(Value("/maestro_button/xpad/DPAD_UP", 0, 0), "gamepad dpad up release");
        Check(Value("/maestro_hat/xpad/dpad", 0), "gamepad dpad release maps hat neutral");
        Check(Value("/maestro_hat/xpad/dpad", 7, 7), "gamepad hat set west");
        Check(Value("/maestro_button/xpad/LEFT", 1), "gamepad left alias reads from hat");
        Call("/maestro_delete", "xpad");
        Check(backend.Devices().Count == 0, "delete gamepad device");
        Check(Call("/maestro_create_custom", "hotas", "Custom HOTAS", 128, 8, 4).Address == "/maestro_created", "custom create");
        Check(Value("/maestro_button/hotas/128", 1, 0.1f), "button 128");
        Check(Value("/maestro_axis/hotas/Dial", 1f, 1f), "eighth axis");
        Check(Value("/maestro_hat/hotas/4", 8, 8), "fourth hat");
        var full = backend.State("hotas");
        Check(full.PressedButtons!.SequenceEqual(new[] { 128 }) && full.Hats!.SequenceEqual(new[] { 0, 0, 0, 8 }) && full.Axes["Dial"] == 1f, "maximum control counts");
        Check(Value("/maestro_axis/hotas/X", 0.5f), "custom neutral axis query");
        Check(Value("/maestro_hat/hotas/4", 0, 0), "hat center");
        Check(Control("/maestro_button/hotas/129", 1).Address == "/maestro_error", "custom button bounds");
        Check(Control("/maestro_hat/hotas/5", 1).Address == "/maestro_error", "custom hat bounds");
        var raw = new CustomJoystick(128, 8, 4);
        raw.Button(128, true, _ => { }); raw.Axis("Dial", 1f, _ => { }); raw.Hat(4, 8, _ => { });
        byte[] frame = raw.Report();
        Check(frame.Length == 36 && frame[15] == 0x80 && frame[30] == 0xFF && frame[31] == 0x7F && frame[32] == 255 && frame[35] == 7,
            "raw report offsets");
        bool rollback = false;
        try { raw.Button(1, true, _ => throw new InvalidOperationException("SDK failure")); } catch (InvalidOperationException) { rollback = true; }
        Check(rollback && raw.Report()[0] == 0, "failed SDK submit rolls back state");
        Check(HidAxisDefaults.Neutral(raw.Descriptor(), 0x0130) == 0.5f, "descriptor signed axis default");
        Call("/maestro_remove", "hotas");
        Check(router.Handle(new("/maestro_definitions", ++id), "client").Any(r => r.Address == "/maestro_definition"), "inactive definitions listed");
        Check(Call("/maestro_rename", "hotas", "Inactive renamed").Address == "/maestro_renamed", "rename inactive");
        Check(Call("/maestro_activate", "hotas").Address == "/maestro_created", "reactivate custom");
        Check(backend.Devices().Single().Name == "Inactive renamed", "reactivated name");
        Call("/maestro_delete", "hotas");
        Check(Call("/maestro_activate", "hotas").Address == "/maestro_error", "delete erases definition");
        string tempDir = Path.Combine(Path.GetTempPath(), "maestro-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var before = new MockBackend();
            var store = new DefinitionCatalog(before, 8, Path.Combine(tempDir, "devices.json"));
            store.DefineAndActivate(new("persist", "custom", "custom-persist", "Persistent", 128, 8, 4));
            store.DefineAndActivate(new("inactive", "profile", "mock-gamepad", "mock-gamepad")); store.Deactivate("inactive");
            using var after = new MockBackend();
            var restored = new DefinitionCatalog(after, 8, Path.Combine(tempDir, "devices.json")); restored.Restore();
            Check(restored.Definitions().Count == 2 && after.Devices().Single().Id == "persist", "disk restore enabled state");
            Check(after.Capabilities("persist").HatCount == 4 && after.State("persist").PressedButtons!.Length == 0, "restored layout is neutral");
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
        Console.WriteLine($"PASS: {checks} protocol/router/report checks (mock backend; no HID hardware tested)");
    }
    private static byte[] Bundle(byte[] element)
    {
        byte[] result = new byte[20 + element.Length]; "#bundle\0"u8.CopyTo(result); result[15] = 1;
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(16), element.Length); element.CopyTo(result, 20); return result;
    }
}
