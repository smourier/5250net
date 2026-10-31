namespace Terminal5250;

internal static class GdsRecord
{
    internal const int _headerLength = 10;
    private const ushort _recordType = 0x12A0;
    private const int _fixedHeaderLength = 6;
    private const byte _variableHeaderLength = 4;
    private const int _flagsOffset = 7;
    private const int _operationOffset = 9;

    internal static byte[] Create(ReadOnlySpan<byte> payload, byte operation = 0, byte flags = 0)
    {
        var record = new byte[checked(_headerLength + payload.Length)];
        if (record.Length > ushort.MaxValue)
            throw new InvalidDataException(Resources.RecordTooLarge);

        BinaryPrimitives.WriteUInt16BigEndian(record, (ushort)record.Length);
        BinaryPrimitives.WriteUInt16BigEndian(record.AsSpan(2), _recordType);
        record[_fixedHeaderLength] = _variableHeaderLength;
        record[_flagsOffset] = flags;
        record[_operationOffset] = operation;
        payload.CopyTo(record.AsSpan(_headerLength));
        return record;
    }

    internal static int Validate(ReadOnlySpan<byte> record)
    {
        if (record.Length < _headerLength || BinaryPrimitives.ReadUInt16BigEndian(record) != record.Length ||
            BinaryPrimitives.ReadUInt16BigEndian(record[2..]) != _recordType || record[_fixedHeaderLength] < _variableHeaderLength ||
            _fixedHeaderLength + record[_fixedHeaderLength] > record.Length)
            throw new InvalidDataException(Resources.BadRecord);

        return _fixedHeaderLength + record[_fixedHeaderLength];
    }
}
