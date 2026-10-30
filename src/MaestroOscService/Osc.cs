using System.Buffers.Binary;
using System.Text;

namespace GEXMaestroBridgeService;

public sealed record OscMessage(string Address, params object?[] Arguments);

// OSC wire format: 32-bit alignment, network byte order, strict UTF-8.
public static class OscCodec
{
    public const int MaxPacketBytes = 60000;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static IReadOnlyList<OscMessage> Decode(byte[] packet)
    {
        if (packet.Length == 0 || packet.Length > MaxPacketBytes || packet.Length % 4 != 0)
            throw new FormatException("Invalid OSC packet length");
        var result = new List<OscMessage>();
        ReadPacket(packet, 0, result);
        return result;
    }

    private static void ReadPacket(ReadOnlySpan<byte> data, int depth, List<OscMessage> output)
    {
        if (depth > 8) throw new FormatException("Bundle nesting exceeds 8");
        int offset = 0;
        string address = ReadString(data, ref offset);
        if (address == "#bundle")
        {
            Require(data, offset, 8);
            ulong timeTag = BinaryPrimitives.ReadUInt64BigEndian(data[offset..]);
            offset += 8;
            if (timeTag != 1) throw new FormatException("Only immediate OSC bundles (timetag 1) are supported");
            while (offset < data.Length)
            {
                int size = ReadInt(data, ref offset);
                if (size <= 0 || size % 4 != 0) throw new FormatException("Invalid bundle element size");
                Require(data, offset, size);
                ReadPacket(data.Slice(offset, size), depth + 1, output);
                offset += size;
            }
            return;
        }
        if (address.Length > 512) throw new FormatException("OSC address exceeds 512 characters");
        if (!address.StartsWith('/')) throw new FormatException("OSC address must start with /");
        string tags = ReadString(data, ref offset);
        if (!tags.StartsWith(',')) throw new FormatException("Missing OSC type tags");
        var args = new List<object?>();
        foreach (char tag in tags.AsSpan(1))
        {
            object? value;
            switch (tag)
            {
                case 'i': value = ReadInt(data, ref offset); break;
                case 'f': value = BitConverter.Int32BitsToSingle(ReadInt(data, ref offset)); break;
                case 's': value = ReadString(data, ref offset); break;
                case 'T': value = true; break;
                case 'F': value = false; break;
                case 'N': value = null; break;
                case 'h':
                    Require(data, offset, 8);
                    value = BinaryPrimitives.ReadInt64BigEndian(data[offset..]); offset += 8; break;
                case 'd':
                    Require(data, offset, 8);
                    value = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(data[offset..]));
                    offset += 8; break;
                default: throw new FormatException($"Unsupported OSC type tag: {tag}");
            }
            args.Add(value);
            if (args.Count > 64) throw new FormatException("Too many OSC arguments");
        }
        if (offset != data.Length) throw new FormatException("Trailing bytes after OSC message");
        if (output.Count >= 256) throw new FormatException("Too many messages in bundle");
        output.Add(new OscMessage(address, args.ToArray()));
    }

    public static byte[] Encode(OscMessage message)
    {
        using var stream = new MemoryStream();
        WriteString(stream, message.Address);
        var tags = new StringBuilder(",");
        foreach (object? arg in message.Arguments)
            tags.Append(arg switch { int => 'i', float => 'f', string => 's', bool b => b ? 'T' : 'F',
                null => 'N', long => 'h', double => 'd', _ => throw new ArgumentException("Unsupported OSC value") });
        WriteString(stream, tags.ToString());
        Span<byte> buffer = stackalloc byte[8];
        foreach (object? arg in message.Arguments)
        {
            switch (arg)
            {
                case int n: BinaryPrimitives.WriteInt32BigEndian(buffer, n); stream.Write(buffer[..4]); break;
                case float f: BinaryPrimitives.WriteInt32BigEndian(buffer, BitConverter.SingleToInt32Bits(f)); stream.Write(buffer[..4]); break;
                case string s: WriteString(stream, s); break;
                case long n: BinaryPrimitives.WriteInt64BigEndian(buffer, n); stream.Write(buffer); break;
                case double d: BinaryPrimitives.WriteInt64BigEndian(buffer, BitConverter.DoubleToInt64Bits(d)); stream.Write(buffer); break;
            }
        }
        if (stream.Length > MaxPacketBytes) throw new ArgumentException("OSC response too large");
        return stream.ToArray();
    }

    private static string ReadString(ReadOnlySpan<byte> data, ref int offset)
    {
        Require(data, offset, 1);
        int length = data[offset..].IndexOf((byte)0);
        if (length < 0) throw new FormatException("Unterminated OSC string");
        int padded = checked((length + 4) & ~3);
        Require(data, offset, padded);
        for (int i = length; i < padded; i++)
            if (data[offset + i] != 0) throw new FormatException("Invalid OSC string padding");
        string value = Utf8.GetString(data.Slice(offset, length));
        offset += padded;
        return value;
    }
    private static int ReadInt(ReadOnlySpan<byte> data, ref int offset)
    {
        Require(data, offset, 4);
        int value = BinaryPrimitives.ReadInt32BigEndian(data[offset..]); offset += 4; return value;
    }
    private static void Require(ReadOnlySpan<byte> data, int offset, int size)
    {
        if (size < 0 || offset < 0 || offset > data.Length - size) throw new FormatException("Truncated OSC packet");
    }
    private static void WriteString(Stream stream, string value)
    {
        if (value.Contains('\0')) throw new ArgumentException("OSC strings cannot contain NUL");
        byte[] bytes = Utf8.GetBytes(value); stream.Write(bytes); stream.WriteByte(0);
        while (stream.Position % 4 != 0) stream.WriteByte(0);
    }
}
