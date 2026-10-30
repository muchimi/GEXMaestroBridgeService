using System.Text.Json;

namespace GEXMaestroBridgeService;

public sealed record DeviceDefinition(string Id, string Kind, string ProfileId, string Name,
    int Buttons = 0, int Axes = 0, int Hats = 0, bool Enabled = true);

// Configuration survives restarts; input state intentionally restarts neutral.
public sealed class DefinitionCatalog(IMaestroBackend backend, int maxDevices, string? path = null)
{
    private Dictionary<string, DeviceDefinition> _items = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public IReadOnlyList<DeviceDefinition> Definitions() => _items.Values.OrderBy(d => d.Id, StringComparer.Ordinal).ToArray();

    public void Restore()
    {
        if (path is null || !File.Exists(path)) return;
        var rows = JsonSerializer.Deserialize<DeviceDefinition[]>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Device definitions file is empty");
        if (rows.Length > 256) throw new InvalidDataException("Too many saved definitions");
        var loaded = new Dictionary<string, DeviceDefinition>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            Validate(row);
            if (!loaded.TryAdd(row.Id, row)) throw new InvalidDataException("Duplicate device definition: " + row.Id);
        }
        if (loaded.Values.Count(d => d.Enabled) > maxDevices) throw new InvalidDataException("Saved enabled devices exceed MaxDevices");
        _items = loaded;
        foreach (var row in Definitions().Where(d => d.Enabled)) ActivateBackend(row);
    }
    public DeviceInfo DefineAndActivate(DeviceDefinition definition)
    {
        Validate(definition);
        if (_items.TryGetValue(definition.Id, out var old) && (old with { Enabled = true }) != (definition with { Enabled = true }))
            throw new InvalidOperationException("ID has another saved definition; delete it before changing its layout/profile");
        Limit(definition.Id);
        bool wasActive = backend.Devices().Any(d => d.Id == definition.Id);
        var info = ActivateBackend(definition);
        try { Save(_items.Values.Where(d => d.Id != definition.Id).Append(definition with { Enabled = true })); }
        catch { if (!wasActive) backend.Remove(definition.Id); throw; }
        return info;
    }
    public DeviceInfo Activate(string id)
    {
        var d = Get(id); return DefineAndActivate(d with { Enabled = true });
    }
    public void Deactivate(string id)
    {
        var d = Get(id);
        // Save first; on hardware failure retain the prior definition where possible.
        Save(_items.Values.Select(x => x.Id == id ? x with { Enabled = false } : x));
        try { if (backend.Devices().Any(x => x.Id == id)) backend.Remove(id); }
        catch { Save(_items.Values.Select(x => x.Id == id ? d : x)); throw; }
    }
    public void Delete(string id)
    {
        var d = Get(id);
        Save(_items.Values.Where(x => x.Id != id));
        try { if (backend.Devices().Any(x => x.Id == id)) backend.Remove(id); }
        catch { Save(_items.Values.Append(d)); throw; }
    }
    public DeviceDefinition Rename(string id, string name)
    {
        var old = Get(id); var updated = old with { Name = name }; Validate(updated);
        if (old.Name == name) return old;
        bool active = backend.Devices().Any(d => d.Id == id);
        if (active)
        {
            backend.Remove(id);
            try { ActivateBackend(updated); }
            catch { ActivateBackend(old); throw; }
        }
        try { Save(_items.Values.Select(d => d.Id == id ? updated : d)); }
        catch
        {
            if (active) { backend.Remove(id); ActivateBackend(old); }
            throw;
        }
        return updated;
    }
    private DeviceDefinition Get(string id) => _items.TryGetValue(id, out var d) ? d : throw new KeyNotFoundException("Unknown definition: " + id);
    private void Limit(string id)
    {
        var devices = backend.Devices();
        if (devices.Count >= maxDevices && !devices.Any(d => d.Id == id)) throw new InvalidOperationException("Configured active device limit reached");
        if (!_items.ContainsKey(id) && _items.Count >= 256) throw new InvalidOperationException("Definition limit reached (256)");
    }
    private DeviceInfo ActivateBackend(DeviceDefinition d) => d.Kind == "custom"
        ? backend.CreateCustom(d.Id, d.Name, d.Buttons, d.Axes, d.Hats) : backend.Create(d.Id, d.ProfileId, d.Name);
    private static void Validate(DeviceDefinition d)
    {
        BackendValidation.DeviceId(d.Id);
        if (string.IsNullOrWhiteSpace(d.Name) || d.Name.Length > 128) throw new ArgumentException("Device name must have 1..128 characters");
        if (d.Kind == "custom")
        {
            CustomJoystick.Validate(d.Buttons, d.Axes, d.Hats);
            if (string.IsNullOrWhiteSpace(d.Name) || d.Name.Length > 128) throw new ArgumentException("Custom name must have 1..128 characters");
        }
        else if (d.Kind != "profile" || string.IsNullOrWhiteSpace(d.ProfileId)) throw new ArgumentException("Invalid definition kind/profile ID");
    }
    private void Save(IEnumerable<DeviceDefinition> rows)
    {
        var next = rows.ToDictionary(d => d.Id, StringComparer.Ordinal);
        if (path is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] data = JsonSerializer.SerializeToUtf8Bytes(next.Values.OrderBy(d => d.Id), Json);
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                { file.Write(data); file.Flush(flushToDisk: true); }
                File.Move(temp, path, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        _items = next;
    }
}
