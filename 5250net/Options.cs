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
    private const string _hostArgument = "HOST";
    private const string _shortHelpOption = "-h";
    private const string _questionHelpOption = "-?";
    private const int _defaultCodePage = 37;
    private const int _maximumNameLength = 10;
    private const int _telnetPort = 23;
    private const int _tlsPort = 992;

    internal string Host { get; private set; } = string.Empty;
    internal int Port { get; private set; }
    internal bool Tls { get; private set; }
    internal bool Wide { get; private set; }
    internal bool Help { get; private set; }
    internal bool Version { get; private set; }
    internal string Device { get; private set; } = string.Empty;
    internal int CodePage { get; private set; } = _defaultCodePage;
    internal string Keyboard { get; private set; } = _defaultKeyboard;

    internal static Options Parse(string[] args)
    {
        var host = new Argument<string?>(_hostArgument)
        {
            Arity = ArgumentArity.ZeroOrOne,
        };

        var port = new Option<int?>(_portOption);
        var tls = new Option<bool>(_tlsOption);
        var wide = new Option<bool>(_wideOption);
        var help = new Option<bool>(_helpOption, _shortHelpOption, _questionHelpOption);
        var version = new Option<bool>(_versionOption);
        var device = new Option<string>(_deviceOption)
        {
            DefaultValueFactory = _ => string.Empty,
        };

        var codePage = new Option<int>(_codePageOption)
        {
            DefaultValueFactory = _ => _defaultCodePage,
        };

        var keyboard = new Option<string>(_keyboardOption)
        {
            DefaultValueFactory = _ => _defaultKeyboard,
        };

        var command = new RootCommand();
        command.Options.Clear();
        command.Arguments.Add(host);
        command.Options.Add(port);
        command.Options.Add(tls);
        command.Options.Add(wide);
        command.Options.Add(help);
        command.Options.Add(version);
        command.Options.Add(device);
        command.Options.Add(codePage);
        command.Options.Add(keyboard);
        var parsed = command.Parse(args);
        if (parsed.Errors.Count != 0)
            throw new ArgumentException(string.Join(Environment.NewLine, parsed.Errors.Select(error => error.Message)));

        var options = new Options
        {
            Host = parsed.GetValue(host) ?? string.Empty,
            Tls = parsed.GetValue(tls),
            Wide = parsed.GetValue(wide),
            Help = args.Length == 0 || parsed.GetValue(help),
            Version = parsed.GetValue(version),
            Device = parsed.GetValue(device) ?? string.Empty,
            CodePage = parsed.GetValue(codePage),
            Keyboard = parsed.GetValue(keyboard) ?? _defaultKeyboard,
        };

        if (options.Help || options.Version)
            return options;

        if (string.IsNullOrWhiteSpace(options.Host))
            throw new ArgumentException(Resources.HostRequired);

        options.Port = parsed.GetValue(port) ?? (options.Tls ? _tlsPort : _telnetPort);
        ValidateNumber(options.Port, _portOption);
        ValidateNumber(options.CodePage, _codePageOption);
        options.Device = ValidateName(options.Device, _deviceOption, false);
        options.Keyboard = ValidateName(options.Keyboard, _keyboardOption, true);
        return options;
    }

    private static void ValidateNumber(int value, string option)
    {
        if (value is < 1 or > ushort.MaxValue)
            throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, Resources.InvalidValue, option, value));
    }

    private static string ValidateName(string value, string option, bool required)
    {
        if (required && value.Length == 0 || value.Length > _maximumNameLength ||
            value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '$' and not '#' and not '@'))
            throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, Resources.InvalidValue, option, value));

        return value.ToUpperInvariant();
    }
}
