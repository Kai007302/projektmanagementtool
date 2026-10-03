using System.Text;

namespace ProjectHub.Api.Modules.Whiteboard;

/// <summary>
/// Managed check of a Yjs update (v1 encoding) before it reaches the native decoder. yrs can crash the whole
/// process on malformed input (found by fuzzing YDotNet 0.6), so untrusted bytes never go there unchecked.
/// The check is stricter than Yjs itself: only what a whiteboard document uses is accepted (ADR 0009):
/// one root map <c>objects</c>, nested maps, plain values (<c>ContentAny</c>) and deletions.
/// </summary>
public static class YjsUpdateValidator
{
    private const int MaxAnyDepth = 16;

    // yrs keeps clocks as 32-bit numbers; larger values or overflowing ranges are never produced by Yjs.
    private const ulong MaxClock = uint.MaxValue;

    // Struct kinds (lower five bits of the info byte).
    private const int Gc = 0;
    private const int ContentDeleted = 1;
    private const int ContentType = 7;
    private const int ContentAny = 8;
    private const int Skip = 10;

    private const int TypeRefMap = 1;

    public static bool IsWellFormed(ReadOnlySpan<byte> update)
    {
        var reader = new Reader(update);
        return ReadStructs(ref reader) && ReadDeleteSet(ref reader) && reader.AtEnd;
    }

    private static bool ReadStructs(ref Reader reader)
    {
        if (!reader.Count(out var clients))
        {
            return false;
        }

        for (ulong i = 0; i < clients; i++)
        {
            if (!reader.Count(out var structs) || !reader.VarUint(out _) || !reader.VarUint(out var clock) || clock > MaxClock)
            {
                return false;
            }

            for (ulong s = 0; s < structs; s++)
            {
                if (!ReadStruct(ref reader, out var length) || length == 0 || (clock += length) > MaxClock)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ReadStruct(ref Reader reader, out ulong length)
    {
        length = 0;
        if (!reader.Byte(out var info))
        {
            return false;
        }

        var kind = info & 0b1_1111;
        switch (kind)
        {
            case Gc:
            case Skip:
                return info == kind && reader.VarUint(out length);
        }

        var hasOrigin = (info & 0x80) != 0;
        var hasRightOrigin = (info & 0x40) != 0;
        var hasParentSub = (info & 0x20) != 0;
        if (hasOrigin && !ReadId(ref reader) || hasRightOrigin && !ReadId(ref reader))
        {
            return false;
        }

        if (!hasOrigin && !hasRightOrigin)
        {
            if (!reader.VarUint(out var parentIsRoot) || parentIsRoot > 1)
            {
                return false;
            }

            if (parentIsRoot == 1
                ? !reader.String(out var root) || root != WhiteboardDocuments.ObjectsMap
                : !ReadId(ref reader))
            {
                return false;
            }

            if (hasParentSub && !reader.String(out _))
            {
                return false;
            }
        }

        switch (kind)
        {
            case ContentDeleted:
                return reader.VarUint(out length);
            case ContentType:
                length = 1;
                return reader.VarUint(out var typeRef) && typeRef == TypeRefMap;
            case ContentAny:
                if (!reader.Count(out length))
                {
                    return false;
                }

                for (ulong i = 0; i < length; i++)
                {
                    if (!ReadAny(ref reader, 0))
                    {
                        return false;
                    }
                }

                return true;
            default:
                return false;
        }
    }

    private static bool ReadDeleteSet(ref Reader reader)
    {
        if (!reader.Count(out var clients))
        {
            return false;
        }

        for (ulong i = 0; i < clients; i++)
        {
            if (!reader.VarUint(out _) || !reader.Count(out var ranges))
            {
                return false;
            }

            for (ulong r = 0; r < ranges; r++)
            {
                if (!reader.VarUint(out var clock) || !reader.VarUint(out var length) || clock > MaxClock || length > MaxClock - clock)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ReadId(ref Reader reader) => reader.VarUint(out _) && reader.VarUint(out var clock) && clock <= MaxClock;

    /// <summary>A lib0 <c>any</c> value, limited to what JSON-like whiteboard fields need.</summary>
    private static bool ReadAny(ref Reader reader, int depth)
    {
        if (depth > MaxAnyDepth || !reader.Byte(out var type))
        {
            return false;
        }

        switch (type)
        {
            case 127: // undefined
            case 126: // null
            case 121: // false
            case 120: // true
                return true;
            case 125: // integer
                return reader.VarInt();
            case 124: // float32
                return reader.Skip(4);
            case 123: // float64
                return reader.Skip(8);
            case 119:
                return reader.String(out _);
            case 118: // object
                if (!reader.Count(out var fields))
                {
                    return false;
                }

                for (ulong i = 0; i < fields; i++)
                {
                    if (!reader.String(out _) || !ReadAny(ref reader, depth + 1))
                    {
                        return false;
                    }
                }

                return true;
            case 117: // array
                if (!reader.Count(out var items))
                {
                    return false;
                }

                for (ulong i = 0; i < items; i++)
                {
                    if (!ReadAny(ref reader, depth + 1))
                    {
                        return false;
                    }
                }

                return true;
            default: // bigint and binary are not used by whiteboards
                return false;
        }
    }

    private ref struct Reader(ReadOnlySpan<byte> bytes)
    {
        private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        private readonly ReadOnlySpan<byte> bytes = bytes;
        private int position;

        public readonly bool AtEnd => position == bytes.Length;

        private readonly int Remaining => bytes.Length - position;

        public bool Byte(out byte value)
        {
            value = 0;
            if (Remaining < 1)
            {
                return false;
            }

            value = bytes[position++];
            return true;
        }

        public bool Skip(int count)
        {
            if (Remaining < count)
            {
                return false;
            }

            position += count;
            return true;
        }

        /// <summary>Unsigned LEB128 as written by lib0, at most 53 bits (the largest safe JavaScript integer).</summary>
        public bool VarUint(out ulong value)
        {
            value = 0;
            for (var shift = 0; shift < 56; shift += 7)
            {
                if (!Byte(out var b))
                {
                    return false;
                }

                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return value < 1UL << 53;
                }
            }

            return false;
        }

        /// <summary>A count of following elements; each takes at least one byte, so it cannot exceed what is left.</summary>
        public bool Count(out ulong value) => VarUint(out value) && value <= (ulong)Remaining;

        /// <summary>Signed lib0 integer: sign in bit 6 of the first byte.</summary>
        public bool VarInt()
        {
            if (!Byte(out var first))
            {
                return false;
            }

            var more = (first & 0x80) != 0;
            for (var shift = 6; more; shift += 7)
            {
                if (shift > 53 || !Byte(out var b))
                {
                    return false;
                }

                more = (b & 0x80) != 0;
            }

            return true;
        }

        public bool String(out string value)
        {
            value = string.Empty;
            if (!VarUint(out var length) || length > (ulong)Remaining)
            {
                return false;
            }

            try
            {
                value = StrictUtf8.GetString(bytes.Slice(position, (int)length));
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            position += (int)length;
            return true;
        }
    }
}
