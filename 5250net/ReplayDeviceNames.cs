namespace Terminal5250;

internal static class ReplayDeviceNames
{
    private const string _separator = "_";
    private const int _maximumNameLength = 10;
    internal const int _maximumNumber = 99999999;

    internal static string Format(string machineName, int number)
    {
        if (number < 1 || number > _maximumNumber)
            throw new ArgumentOutOfRangeException(nameof(number));

        var machine = Options.DefaultDevice(machineName);
        var suffix = _separator + number.ToString(CultureInfo.InvariantCulture);
        return machine[..Math.Min(machine.Length, _maximumNameLength - suffix.Length)] + suffix;
    }
}
