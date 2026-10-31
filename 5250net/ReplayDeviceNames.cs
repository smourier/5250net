namespace Terminal5250;

internal static class ReplayDeviceNames
{
    private const string _counterPrefix = "devices.";
    private const string _counterExtension = ".counter";
    private const string _separator = "_";
    private const int _maximumNameLength = 10;
    private const int _maximumNumber = 99999999;
    private const int _counterLength = sizeof(int);
    private const int _lockAttempts = 100;
    private const int _lockRetryMilliseconds = 10;

    internal static string Next(string directory)
    {
        var machine = Options.DefaultDevice(Environment.MachineName);
        var path = Path.Combine(directory, _counterPrefix + machine + _counterExtension);

        // the counter persists across groups so a new batch cannot reuse a still-connected device name.
        using var counter = OpenCounter(path);
        Span<byte> bytes = stackalloc byte[_counterLength];
        var previous = 0;
        if (counter.Length != 0)
        {
            if (counter.Length != _counterLength)
                throw new InvalidDataException(Resources.ReplayDeviceCounter);

            counter.ReadExactly(bytes);
            previous = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        }

        if (previous < 0 || previous >= _maximumNumber)
            throw new InvalidDataException(Resources.ReplayDeviceCounter);

        var number = previous + 1;
        BinaryPrimitives.WriteInt32LittleEndian(bytes, number);
        counter.Position = 0;
        counter.Write(bytes);
        counter.Flush(true);
        return Format(machine, number);
    }

    internal static string Format(string machineName, int number)
    {
        if (number < 1 || number > _maximumNumber)
            throw new ArgumentOutOfRangeException(nameof(number));

        var machine = Options.DefaultDevice(machineName);
        var suffix = _separator + number.ToString(CultureInfo.InvariantCulture);
        return machine[..Math.Min(machine.Length, _maximumNameLength - suffix.Length)] + suffix;
    }

    private static FileStream OpenCounter(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < _lockAttempts)
            {
                Thread.Sleep(_lockRetryMilliseconds);
            }
        }
    }
}
