using System.Text.Json;

namespace GEXMaestroBridgeService;

public static class GexProductIdentityTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool pass, string name) { if (!pass) throw new Exception("FAIL: " + name); checks++; }
        var ids = Enumerable.Range(1, 16).Select(n => $"gex-{n}")
            .Concat(Enumerable.Range(1, 4).Select(n => $"gex-gp-{n}")).ToArray();
        Check(ids.Select(GexDeviceIdentity.ProductId).Distinct().Count() == 20, "all twenty GEX PIDs are distinct");
        Check(ids.Select(GexDeviceIdentity.ProductId).SequenceEqual(
            Enumerable.Range(0x4701, 16).Concat(Enumerable.Range(0x4801, 4)).Select(n => (int?)n)), "PID families and one-based slot bytes");
        Check(new[] { "gex-0", "gex-01", "gex-257", "gex-gp-0", "gex-gp-5", "foreign" }
            .All(id => GexDeviceIdentity.ProductId(id) == null), "only canonical GEX service IDs have slot PIDs");
        Check(ids.Reverse().OrderBy(GexDeviceIdentity.SortOrder).SequenceEqual(ids), "mixed device families restore in numeric order");

#if !MOCK_ONLY
        // Build real SDK profiles without installing a driver or creating devices.
        using (var context = new HIDMaestro.HMContext())
        {
            context.LoadDefaultProfiles();
            var source = context.GetProfile("xbox-360-wireless")!;
            foreach (string id in ids.Skip(16))
            {
                var profile = HidMaestroBackend.PrepareProfile(source, id, id);
                Check(profile.VendorId == 1 && profile.ProductId == GexDeviceIdentity.ProductId(id), "gamepad HID has its unique GEX PID");
                Check(profile.Name == id && profile.ProductString == id && profile.DisplayName == id, "gamepad Windows name fields agree");
                Check(profile.DescriptorHex == source.DescriptorHex && profile.TriggerMode == source.TriggerMode,
                    "gamepad descriptor and trigger semantics preserved");
                Check(JsonSerializer.Serialize(profile.XInput) == JsonSerializer.Serialize(source.XInput),
                    "XUSB wireless companion information, capabilities and binding preserved");
                var inner = typeof(HIDMaestro.HMProfile).GetProperty("Inner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(profile);
                var planType = typeof(HIDMaestro.HMContext).Assembly.GetType("HIDMaestro.Internal.PresentationPlan", true)!;
                var plan = planType.GetMethod("Of")!.Invoke(null, [inner])!;
                var companionIds = (string[])planType.GetProperty("CompanionHardwareIds")!.GetValue(plan)!;
                Check(companionIds.Contains(@"root\VID_045E&PID_0719&XI_00"), "wireless XUSB INF hardware ID unchanged");
            }
            Check(source.VendorId == 0x045E && source.ProductId != 0x4801, "embedded catalog profile is unchanged");
        }
#endif
        string dir = Path.Combine(Path.GetTempPath(), "gex-products-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "devices.json");
            var legacy = ids.Reverse().Select(id => new DeviceDefinition(id,
                id.StartsWith("gex-gp-") ? "gamepad" : "custom",
                id.StartsWith("gex-gp-") ? "xbox-360-wireless" : "custom-" + id, id, 128, 8, 4));
            File.WriteAllText(file, JsonSerializer.Serialize(legacy));
            using (var backend = new MockBackend())
            {
                var catalog = new DefinitionCatalog(backend, 24, file);
                catalog.Restore();
                Check(catalog.Definitions().Select(d => d.ProductId).SequenceEqual(ids.Select(id => GexDeviceIdentity.ProductId(id)!.Value)),
                    "legacy definitions migrate and persist distinct slot PIDs");
                Check(backend.Devices().OrderBy(d => d.TransportIndex).Select(d => d.Id).SequenceEqual(ids), "mixed devices activate in numeric order");
                Check(ids.All(id => backend.Capabilities(id).ProductId == GexDeviceIdentity.ProductId(id)), "capability inventory reflects hardware PIDs");
                catalog.Rename("gex-2", "Pilot controls");
                catalog.Rename("gex-gp-2", "Copilot controls");
                catalog.DefineAndActivate(new("custom-b", "custom", "custom", "Custom B", 4, 2));
                catalog.DefineAndActivate(new("custom-a", "custom", "custom", "Custom A", 4, 2));
                Check(catalog.Definitions().Single(d => d.Id == "custom-b").ProductId == 0x4901 &&
                      catalog.Definitions().Single(d => d.Id == "custom-a").ProductId == 0x4902, "named custom devices receive distinct persisted PIDs");
                bool duplicateRejected = false;
                try { catalog.DefineAndActivate(new("duplicate", "custom", "custom", "Duplicate", 4, 2,
                    VendorId: 1, ProductId: 0x4901)); }
                catch (InvalidOperationException) { duplicateRejected = true; }
                Check(duplicateRejected && backend.Devices().All(d => d.Id != "duplicate"), "duplicate GEX PID rejected before hardware creation");
                catalog.Deactivate("gex-3");
            }
            using (var restarted = new MockBackend())
            {
                var catalog = new DefinitionCatalog(restarted, 24, file);
                catalog.Restore();
                Check(catalog.Definitions().Single(d => d.Id == "gex-2").ProductId == 0x4702 &&
                      catalog.Definitions().Single(d => d.Id == "gex-2").Name == "Pilot controls", "joystick PID survives rename and restart");
                Check(catalog.Definitions().Single(d => d.Id == "gex-gp-2").ProductId == 0x4802 &&
                      restarted.Capabilities("gex-gp-2").Name == "Copilot controls", "gamepad PID survives rename and restart");
                Check(catalog.Definitions().Single(d => d.Id == "custom-b").ProductId == 0x4901, "allocation survives changed restore order");
                catalog.DefineAndActivate(new("custom-b", "custom", "custom", "Custom B", 4, 2));
                Check(catalog.Definitions().Single(d => d.Id == "custom-b").ProductId == 0x4901, "old client retries reuse assigned custom PID");
                Check(catalog.Definitions().Single(d => d.Id == "gex-3").ProductId == 0x4703 &&
                      restarted.Devices().All(d => d.Id != "gex-3"), "disabled definitions migrate without activation");
                catalog.Activate("gex-3");
                Check(restarted.Capabilities("gex-3").ProductId == 0x4703, "reactivation uses migrated PID");
            }
            var foreign = GexDeviceIdentity.Normalize(new("foreign", "profile", "mock-gamepad", "Foreign", VendorId: 0x1234, ProductId: 0x5678));
            Check(foreign.VendorId == 0x1234 && foreign.ProductId == 0x5678, "unrelated catalog identities preserved");
        }
        finally { Directory.Delete(dir, true); }
        return checks;
    }
}
