namespace GEXMaestroBridgeService;

// Small HID item reader used only for initial axis state, not for report packing.
public static class HidAxisDefaults
{
    public static float Neutral(byte[] descriptor, ushort axis)
    {
        int page = 0, logicalMin = 0;
        var stack = new Stack<(int Page, int Minimum)>();
        var usages = new List<uint>();
        uint? usageMin = null, usageMax = null;
        for (int offset = 0; offset < descriptor.Length;)
        {
            byte prefix = descriptor[offset++];
            if (prefix == 0xFE)
            {
                if (offset + 2 > descriptor.Length) throw new FormatException("Truncated HID long item");
                int count = descriptor[offset]; offset += 2 + count;
                if (offset > descriptor.Length) throw new FormatException("Truncated HID long item");
                continue;
            }
            int size = (prefix & 3) == 3 ? 4 : prefix & 3;
            if (offset + size > descriptor.Length) throw new FormatException("Truncated HID item");
            uint value = 0;
            for (int i = 0; i < size; i++) value |= (uint)descriptor[offset + i] << (8 * i);
            offset += size;
            int type = (prefix >> 2) & 3, tag = prefix >> 4;
            if (type == 1)
            {
                switch (tag)
                {
                    case 0: page = (int)value; break;
                    case 1: logicalMin = size switch { 1 => (sbyte)value, 2 => (short)value, _ => (int)value }; break;
                    case 10: stack.Push((page, logicalMin)); break;
                    case 11:
                        if (stack.Count == 0) throw new FormatException("Unbalanced HID global Pop");
                        (page, logicalMin) = stack.Pop(); break;
                }
            }
            else if (type == 2)
            {
                uint usage = size == 4 ? value : ((uint)page << 16) | value;
                if (tag == 0) usages.Add(usage);
                if (tag == 1) usageMin = usage;
                if (tag == 2) usageMax = usage;
            }
            else if (type == 0)
            {
                uint wanted = ((uint)(axis >> 8) << 16) | (uint)(axis & 255);
                if (tag == 8 && (value & 1) == 0 &&
                    (usages.Contains(wanted) || (usageMin.HasValue && usageMax.HasValue && wanted >= usageMin.Value && wanted <= usageMax.Value)))
                    return logicalMin < 0 ? 0.5f : 0f;
                usages.Clear(); usageMin = usageMax = null;
            }
        }
        return 0.5f;
    }
}
