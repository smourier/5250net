namespace Terminal5250;

internal static class StartupResponse
{
    private const int _responseLength = 73;
    private const int _codeOffset = 16;
    private const int _codeLength = 4;
    private const byte _startupMarker = 0x90;
    private const string _reducedFunction = "I901";
    private const string _started = "I902";
    private const string _signOnRequired = "I906";
    private static readonly Encoding _encoding = CodePagesEncodingProvider.Instance.GetEncoding(37)!;

    internal static bool Handle(ReadOnlySpan<byte> record)
    {
        if (record.Length < 5 || record[4] != _startupMarker)
            return false;

        if (record.Length != _responseLength || BinaryPrimitives.ReadUInt16BigEndian(record) != record.Length ||
            record[2] != 0x12 || record[3] != 0xA0)
            throw new InvalidDataException(Resources.BadRecord);

        var code = _encoding.GetString(record.Slice(_codeOffset, _codeLength));
        if (code is not _reducedFunction and not _started and not _signOnRequired)
            throw new InvalidDataException(string.Format(CultureInfo.CurrentCulture, Resources.StartupFailed, code));

        return true;
    }
}
