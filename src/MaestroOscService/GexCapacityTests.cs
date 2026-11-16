using System.Text.Json;

namespace GEXMaestroBridgeService;

public static class GexCapacityTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool pass, string label) { if (!pass) throw new Exception("FAIL: " + label); checks++; }
        Check(new OscOptions().MaxDevices == 256, "new bridge defaults to full capacity");
        var ids = Enumerable.Range(1, 256).Select(n => $"gex-{n}").ToArray();
        var keys = ids.Select(id => GexDeviceIdentity.KeyFor(id)).ToArray();
        var tokens = keys.Select(GexDeviceIdentity.InstanceToken).ToArray();
        var prefixes = keys.Select(GexDeviceIdentity.ParentIdPrefix).ToArray();
        Check(tokens.Distinct().Count() == 256 && tokens.SequenceEqual(tokens.Order(StringComparer.Ordinal)), "all 256 instance tokens are distinct and numerically ordered");
        Check(prefixes.Distinct().Count() == 256 && prefixes.SequenceEqual(prefixes.Order(StringComparer.Ordinal)), "all 256 HID prefixes follow numeric order");
        Check(ids.Select(GexDeviceIdentity.ProductId).SequenceEqual(Enumerable.Range(0x4701, 256).Select(n => (int?)n)), "PIDs remain sequential through gex-256 at 4800");
        Check(Enumerable.Range(1,4).Select(n => GexDeviceIdentity.ProductId($"gex-gp-{n}")).All(pid => !ids.Select(GexDeviceIdentity.ProductId).Contains(pid)), "gamepad PIDs do not collide at joystick capacity");
        string file = Path.Combine(Path.GetTempPath(), "gex-capacity-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            using (var backend = new MockBackend())
            {
                var catalog = new DefinitionCatalog(backend, 256, file);
                foreach (string id in ids) catalog.DefineAndActivate(new(id,"custom","custom-"+id,id,128,8,4));
                Check(catalog.Definitions().Count == 256 && backend.Devices().Count == 256, "256 controllers created and persisted");
                Check(backend.Devices().Skip(16).All(d => d.FailoverVjoyId == null), "extended devices never take vJoy aliases");
                bool rejected = false;
                try { catalog.DefineAndActivate(new("extra","custom","custom-extra","Extra",1,1)); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected && backend.Devices().Count == 256, "bridge capacity enforced before creating controller 257");
            }
            using (var backend = new MockBackend())
            {
                var catalog = new DefinitionCatalog(backend, 256, file);
                catalog.Restore();
                Check(backend.Devices().OrderBy(d => d.TransportIndex).Select(d => d.Id).SequenceEqual(ids), "restart restores all 256 in numeric order");
                Check(backend.Capabilities("gex-256").ProductId == 0x4800, "last PID survives restart");
            }
        }
        finally { if (File.Exists(file)) File.Delete(file); }
        return checks;
    }
}
