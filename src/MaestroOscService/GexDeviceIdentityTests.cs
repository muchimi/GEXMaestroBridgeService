using System.Text.Json;

namespace GEXMaestroBridgeService;

public static class GexDeviceIdentityTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool pass, string name) { if (!pass) throw new Exception("FAIL: " + name); checks++; }
        string[] ids = Enumerable.Range(1, 16).Select(n => $"gex-{n}").ToArray();
        string[] keys = ids.Select(id => GexDeviceIdentity.KeyFor(id, custom: true)).ToArray();
        string[] tokens = keys.Select(GexDeviceIdentity.InstanceToken).ToArray();
        string[] prefixes = keys.Select(GexDeviceIdentity.ParentIdPrefix).ToArray();
        Check(keys.Distinct().Count() == 16, "sixteen distinct durable GEX identities");
        Check(tokens.SequenceEqual(tokens.Order(StringComparer.Ordinal)), "GEX instance tokens follow numeric device order");
        Check(prefixes.SequenceEqual(prefixes.Order(StringComparer.Ordinal)), "GEX HID parent prefixes follow numeric device order");
        Check(tokens.Select(t => t[..5]).SequenceEqual(Enumerable.Range(0x10, 16).Select(n => $"HM_{n:X2}")),
            "each GEX ID has its own ordered token range");
        Check(GexDeviceIdentity.InstanceToken("GEX:gex-1") == "HM_6CA18125804D3151", "SDK identity hash golden vector");
        for (int i = 0; i < ids.Length; i++)
        {
            Check(GexDeviceIdentity.KeyFor(ids[i]) == keys[i], "layout/profile changes retain GEX identity");
            Check(GexDeviceIdentity.KeyFor(ids[i], true) == keys[i], "repeated creates retain GEX identity");
        }
        Check(GexDeviceIdentity.KeyFor("another-client", true) == "GEX:another-client", "other custom identities unchanged");
        Check(GexDeviceIdentity.KeyFor("gex-gp-1") == "MaestroOscService:gex-gp-1", "gamepad identity namespace unchanged");
        Check(GexDeviceIdentity.JoystickNumber("gex-0") is null && GexDeviceIdentity.JoystickNumber("gex-17") is null &&
              GexDeviceIdentity.JoystickNumber("gex-01") is null, "only canonical joystick IDs 1..16 are ordered");

#if !MOCK_ONLY
        // Compare with the shipped SDK without creating, installing or opening devices.
        Type sdkIdentity = typeof(HIDMaestro.HMContext).Assembly.GetType("HIDMaestro.Internal.DeviceIdentity", throwOnError: true)!;
        var forKey = sdkIdentity.GetMethod("ForKey", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        foreach (string key in keys)
        {
            object identity = forKey.Invoke(null, [key, 0])!;
            Check((string)sdkIdentity.GetProperty("Token")!.GetValue(identity)! == GexDeviceIdentity.InstanceToken(key),
                "ordered token agrees with installed HIDMaestro SDK");
            Check((string)sdkIdentity.GetProperty("ParentIdPrefix")!.GetValue(identity)! == GexDeviceIdentity.ParentIdPrefix(key),
                "ordered HID prefix agrees with installed HIDMaestro SDK");
        }
#endif

        string directory = Path.Combine(Path.GetTempPath(), "gex-identity-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "devices.json");
            var definitions = ids.Reverse().Select(id => new DeviceDefinition(id, "custom", "custom-" + id, "gex-1", 128, 8, 4)).ToArray();
            File.WriteAllText(file, JsonSerializer.Serialize(definitions));
            using var backend = new MockBackend();
            var catalog = new DefinitionCatalog(backend, 16, file);
            catalog.Restore();
            Check(catalog.Definitions().Select(d => d.Id).SequenceEqual(ids), "definition inventory orders 2 before 10");
            Check(backend.Devices().OrderBy(d => d.TransportIndex).Select(d => d.Id).SequenceEqual(ids),
                "saved controllers are physically created in numeric GEX order");
            Check(backend.Devices().All(d => d.Name == d.Id), "old generated GEX names are repaired to their service IDs");
            using var saved = JsonDocument.Parse(File.ReadAllText(file));
            Check(saved.RootElement.EnumerateArray().Select(row => row.GetProperty("name").GetString()).SequenceEqual(ids),
                "corrected generated names and numeric order are persisted");
            Check(backend.Devices().Select(d => d.InstanceToken).SequenceEqual(tokens), "OSC inventory exposes instance tokens");
            Check(backend.Devices().Select(d => d.FailoverVjoyId).SequenceEqual(Enumerable.Range(1, 16).Select(n => (int?)n)),
                "OSC inventory ties each GEX identity to the matching vJoy ID");
            var old = backend.Devices().Single(d => d.Id == "gex-2");
            catalog.Rename("gex-2", "Pilot controls");
            var renamed = backend.Devices().Single(d => d.Id == "gex-2");
            Check(renamed.InstanceToken == old.InstanceToken && renamed.IdentityKey == old.IdentityKey,
                "display-name changes retain the ordered instance identity");
            Check(backend.Devices().Select(d => d.TransportIndex).Distinct().Count() == 16,
                "mock recreation retains distinct transport indexes");
            bool rejected = false;
            try { catalog.Rename("gex-2", "gex-3"); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected && backend.Devices().Single(d => d.Id == "gex-2").Name == "Pilot controls",
                "a generated label cannot contradict its service identity");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        return checks;
    }
}
