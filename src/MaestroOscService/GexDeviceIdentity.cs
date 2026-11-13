using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GEXMaestroBridgeService;

/// <summary>Durable GEX joystick identities whose HID instance prefixes sort 1..16.</summary>
public static class GexDeviceIdentity
{
    private const string HashDomain = "HIDMaestro.DeviceIdentity.v1\n";
    private static readonly Lazy<string[]> OrderedKeys = new(() =>
        Enumerable.Range(1, 16).Select(FindOrderedKey).ToArray());

    public static int? JoystickNumber(string id)
    {
        if (!id.StartsWith("gex-", StringComparison.Ordinal) ||
            !int.TryParse(id.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out int number) ||
            number is < 1 or > 16 || id != $"gex-{number}") return null;
        return number;
    }

    public static int SortOrder(string id) => JoystickNumber(id) ?? int.MaxValue;

    public static string DisplayName(string id, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        // Repair old generated/duplicated labels, while retaining user-defined names.
        return JoystickNumber(id) is not null && name.StartsWith("gex-", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(name.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out _)
            ? id : name;
    }

    public static string KeyFor(string id, bool custom = false) => JoystickNumber(id) is int number
        ? OrderedKeys.Value[number - 1]
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
        for (int nonce = 0; nonce < 65536; nonce++)
        {
            string key = FormattableString.Invariant($"GEX:ordered-joystick:v1:{number:D2}:{nonce}");
            if (Digest(key)[0] == 0x0f + number) return key;
        }
        throw new InvalidOperationException($"Unable to derive ordered identity for gex-{number}");
    }

    public static DeviceInfo Describe(DeviceInfo info, string identityKey) => info with
    {
        IdentityKey = identityKey,
        InstanceToken = InstanceToken(identityKey),
        HidParentIdPrefix = ParentIdPrefix(identityKey),
        FailoverVjoyId = JoystickNumber(info.Id),
    };
}
