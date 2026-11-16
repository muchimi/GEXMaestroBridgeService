using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GEXMaestroBridgeService;

/// <summary>Durable GEX joystick identities whose HID instance prefixes sort numerically through 256.</summary>
public static class GexDeviceIdentity
{
    private const string HashDomain = "HIDMaestro.DeviceIdentity.v1\n";
    public const int MaxDevices = 256;
    private static readonly ConcurrentDictionary<int, string> OrderedKeys = new();

    public static int? JoystickNumber(string id)
    {
        if (!id.StartsWith("gex-", StringComparison.Ordinal) ||
            !int.TryParse(id.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out int number) ||
            number is < 1 or > MaxDevices || id != $"gex-{number}") return null;
        return number;
    }

    // Project-local USB product families, not globally assigned USB vendor IDs.
    public const int VendorId = 0x0001;
    public const int JoystickPidBase = 0x4700;
    public const int GamepadPidBase = 0x4800;

    public static int? GamepadNumber(string id)
    {
        const string prefix = "gex-gp-";
        if (!id.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(id.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ||
            n is < 1 or > 4 || id != $"gex-gp-{n}") return null;
        return n;
    }

    public static int? ProductId(string id) => JoystickNumber(id) is int j ? JoystickPidBase + j
        : GamepadNumber(id) is int g ? GamepadPidBase + g : null;

    public static DeviceDefinition Normalize(DeviceDefinition d)
    {
        d = d with { Name = DisplayName(d.Id, d.Name) };
        // Catalog profiles keep their hardware identity. Only GEX custom
        // joysticks and the bridge's dedicated Xbox gamepads use these IDs.
        bool managed = d.Kind == "custom" && JoystickNumber(d.Id) != null ||
            (d.Kind == "gamepad" || d.Kind == "profile" && d.ProfileId == "xbox-360-wireless") && GamepadNumber(d.Id) != null;
        return managed ? d with { VendorId = VendorId, ProductId = ProductId(d.Id)!.Value } : d;
    }

    public static int SortOrder(string id) => JoystickNumber(id) ??
        (GamepadNumber(id) is int g ? MaxDevices + g : int.MaxValue);

    public static string DisplayName(string id, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        // Repair old generated/duplicated labels, while retaining user-defined names.
        string? prefix = JoystickNumber(id) is not null ? "gex-" : null;
        if (id.StartsWith("gex-gp-", StringComparison.Ordinal) &&
            int.TryParse(id.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out int number) &&
            number is >= 1 and <= 16 && id == $"gex-gp-{number}") prefix = "gex-gp-";
        return prefix is not null && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(name.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out _)
            ? id : name;
    }

    public static string KeyFor(string id, bool custom = false) => JoystickNumber(id) is int number
        ? OrderedKeys.GetOrAdd(number, FindOrderedKey)
        : (custom ? "GEX:" : "MaestroOscService:") + id;

    private static byte[] Digest(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(HashDomain + key.Trim()));

    public static string InstanceToken(string key) => "HM_" + Convert.ToHexString(Digest(key), 0, 8);

    public static string ParentIdPrefix(string key) => $"1&{BinaryPrimitives.ReadUInt32BigEndian(Digest(key)):x}&0";

    private static string FindOrderedKey(int number)
    {
        // SDK tokens are hashes, so zero-padding a service ID does not order them.
        // Reserve byte ranges 0x10..0x1f for GEX 1..16. Their eight-digit HID
        // ParentIdPrefix hashes and sixteen-digit HM_ tokens have the same order.
        // The namespace and first successful nonce are fixed across every life;
        // neither the active device count nor the transport slot enters this key.
        for (int nonce = 0; nonce < 1048576; nonce++)
        {
            string key = FormattableString.Invariant($"GEX:ordered-joystick:v1:{number:D2}:{nonce}");
            byte[] digest = Digest(key);
            // Preserve all original 1..16 keys. Later devices use ascending
            // twelve-bit prefixes, computed lazily, without exhausting a byte.
            if (number <= 16 ? digest[0] == 0x0f + number
                : digest[0] == 0x20 + (number - 17) / 16 && digest[1] >> 4 == (number - 17) % 16)
                return key;
        }
        throw new InvalidOperationException($"Unable to derive ordered identity for gex-{number}");
    }

    public static DeviceInfo Describe(DeviceInfo info, string identityKey) => info with
    {
        IdentityKey = identityKey,
        InstanceToken = InstanceToken(identityKey),
        HidParentIdPrefix = ParentIdPrefix(identityKey),
        FailoverVjoyId = JoystickNumber(info.Id) is int n && n <= 16 ? n : null,
    };
}
