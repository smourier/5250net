namespace Terminal5250;

internal sealed class Options
{
    internal const string _tlsOption = "--tls";
    internal const string _wideOption = "--wide";
    internal const string _portOption = "--port";
    internal const string _codePageOption = "--code-page";
    internal const string _deviceOption = "--device";
    internal const string _keyboardOption = "--keyboard";
    internal const string _helpOption = "--help";
    internal const string _versionOption = "--version";
    internal const string _defaultKeyboard = "USB";
    private const int _defaultCodePage = 37;
    private const int _maximumNameLength = 10;
    private const int _telnetPort = 23;
    private const int _tlsPort = 992;

    internal string _host { get; private set; } = string.Empty;
    internal int _port { get; private set; }
    internal bool _tls { get; private set; }
    internal bool _wide { get; private set; }
    internal string _device { get; private set; } = string.Empty;
    internal int _codePage { get; private set; } = _defaultCodePage;
    internal string _keyboard { get; private set; } = _defaultKeyboard;

    internal static Options Parse(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];
            switch (argument)
            {
                case _tlsOption:
                    options._tls = true;
                    break;

                case _wideOption:
                    options._wide = true;
                    break;

                case _portOption:
                case _codePageOption:
                case _deviceOption:
                case _keyboardOption:
                    if (++i == args.Length)
                        throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, Resources.InvalidOption, argument));

                    var value = args[i];
                    if (argument == _portOption || argument == _codePageOption)
                    {
                        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
                            number < 1 || number > ushort.MaxValue)
                            throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, Resources.InvalidValue, argument, value));

                        if (argument == _portOption)
                        {
                            options._port = number;
                        }
                        else
                        {
                            options._codePage = number;
                        }
                    }
                    else
                    {
                        if (value.Length is < 1 or > _maximumNameLength ||
                            value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '$' and not '#'))
                            throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, Resources.InvalidValue, argument, value));

                        if (argument == _deviceOption)
                        {
                            options._device = value.ToUpperInvariant();
                        }
                        else
                        {
                            options._keyboard = value.ToUpperInvariant();
                        }
                    }

                    break;

                default:
                    if (argument.StartsWith('-') || options._host.Length != 0)
                        throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, Resources.InvalidOption, argument));

                    options._host = argument;
                    break;
            }
        }

        if (options._host.Length == 0)
            throw new ArgumentException(Resources.HostRequired);

        if (options._port == 0)
        {
            options._port = options._tls ? _tlsPort : _telnetPort;
        }

        return options;
    }
}
