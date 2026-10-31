namespace Terminal5250;

internal sealed class Options
{
    internal const string _tlsOption = "--tls";
    internal const string _tlsCertificateOption = "--tls-certificate";
    internal const string _tlsTrustAllOption = "--tls-trust-all";
    internal const string _wideOption = "--wide";
    internal const string _portOption = "--port";
    internal const string _codePageOption = "--code-page";
    internal const string _deviceOption = "--device";
    internal const string _keyboardOption = "--keyboard";
    internal const string _captureDirectoryOption = "--capture-directory";
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

    internal FileInfo? _tlsCertificate { get; private set; }
    internal bool _tlsTrustAll { get; private set; }
    internal string? _captureDirectory { get; private set; }
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
        var tlsCertificate = new Option<FileInfo>(_tlsCertificateOption);
        tlsCertificate.AcceptExistingOnly();
        var tlsTrustAll = new Option<bool>(_tlsTrustAllOption);
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

        var captureDirectory = new Option<string?>(_captureDirectoryOption);
        var command = new RootCommand();
        command.Options.Clear();
        command.Arguments.Add(host);
        command.Options.Add(port);
        command.Options.Add(tls);
        command.Options.Add(tlsCertificate);
        command.Options.Add(tlsTrustAll);
        command.Options.Add(wide);
        command.Options.Add(help);
        command.Options.Add(version);
        command.Options.Add(device);
        command.Options.Add(codePage);
        command.Options.Add(keyboard);
        command.Options.Add(captureDirectory);
        var parsed = command.Parse(args);
        if (parsed.Errors.Count != 0)
            throw new ArgumentException(string.Join(Environment.NewLine, parsed.Errors.Select(error => error.Message)));

        var options = new Options
        {
            _captureDirectory = parsed.GetValue(captureDirectory),
            Host = parsed.GetValue(host) ?? string.Empty,
            Tls = parsed.GetValue(tls),
            _tlsCertificate = parsed.GetValue(tlsCertificate),
            _tlsTrustAll = parsed.GetValue(tlsTrustAll),
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

        if (options._captureDirectory != null && string.IsNullOrWhiteSpace(options._captureDirectory))
            throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, Resources.InvalidValue,
                _captureDirectoryOption, options._captureDirectory));

        if (!options.Tls && (options._tlsCertificate != null || options._tlsTrustAll))
            throw new ArgumentException(Resources.TlsOptionsRequireTls);

        if (options._tlsCertificate != null && options._tlsTrustAll)
            throw new ArgumentException(Resources.TlsOptionsExclusive);

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
