namespace Terminal5250;

internal sealed class Options
{
    internal const string _usersOption = "--users";
    internal const string _usersAlias = "-u";
    internal const string _userIntervalOption = "--user-interval";
    internal const string _userIntervalAlias = "-ui";
    internal const string _recordAlias = "-rec";
    internal const string _replayAlias = "-rep";
    internal const string _deviceAlias = "-d";
    private const string _infoOption = "--info";
    private const string _infoAlias = "-i";
    private const string _tlsAlias = "-t";
    private const string _tlsTrustAllAlias = "-ta";
    private const string _certificateAlias = "-cert";
    private const string _wideAlias = "-w";
    private const string _portAlias = "-p";
    private const string _codePageAlias = "-cp";
    private const string _keyboardAlias = "-k";
    private const string _captureAlias = "-cap";
    private const string _replaySpeedAlias = "-rs";
    private const string _replayTimeoutAlias = "-rt";
    internal const string _tlsOption = "--tls";
    internal const string _tlsCertificateOption = "--tls-certificate";
    internal const string _tlsTrustAllOption = "--tls-trust-all";
    internal const string _wideOption = "--wide";
    internal const string _portOption = "--port";
    internal const string _codePageOption = "--code-page";
    internal const string _deviceOption = "--device";
    internal const string _keyboardOption = "--keyboard";
    internal const string _captureDirectoryOption = "--capture-directory";
    internal const string _recordOption = "--record";
    internal const string _replayOption = "--replay";
    internal const string _replaySpeedOption = "--replay-speed";
    internal const string _replayTimeoutOption = "--replay-timeout";
    internal const string _userNumberOption = "--user-number";
    internal const string _replayUserLimitOption = "--replay-user-limit";
    internal const string _replayGroupOption = "--replay-group";
    internal const double _fullReplayTiming = 100;
    private const double _defaultReplayTimeout = 30;
    private const double _maximumReplayTimeout = 86400;
    internal const string _helpOption = "--help";
    internal const string _versionOption = "--version";
    internal const string _defaultKeyboard = "USB";
    private const string _hostArgument = "HOST";
    private const int _defaultCodePage = 37;
    private const int _maximumNameLength = 10;
    private const string _devicePrefix = "Q";
    private const int _telnetPort = 23;
    private const int _tlsPort = 992;

    internal ParseResult? Invocation { get; private set; }
    internal string? RecordFile { get; private set; }
    internal string? ReplayFile { get; private set; }
    internal int ReplayUserNumber { get; private set; } = 1;
    internal int ReplayUserLimit { get; private set; }
    internal int ReplayUsers { get; private set; } = 1;
    internal double UserInterval { get; private set; }
    internal double ReplaySpeed { get; private set; } = _fullReplayTiming;
    internal double ReplayTimeout { get; private set; } = _defaultReplayTimeout;
    internal string? ReplayGroup { get; private set; }
    internal FileInfo? TlsCertificate { get; private set; }
    internal bool TlsTrustAll { get; private set; }
    internal string? CaptureDirectory { get; private set; }
    internal string Host { get; private set; } = string.Empty;
    internal int Port { get; private set; }
    internal bool Tls { get; private set; }
    internal bool Wide { get; private set; }
    internal bool Help { get; private set; }
    internal bool Version { get; private set; }
    internal bool DeviceSpecified { get; private set; }
    internal string Device { get; set; } = string.Empty;
    internal int CodePage { get; private set; } = _defaultCodePage;
    internal string Keyboard { get; private set; } = _defaultKeyboard;

    internal static Options Parse(string[] args)
    {
        var host = new Argument<string?>(_hostArgument)
        {
            Arity = ArgumentArity.ExactlyOne,
        };

        var info = new Option<bool>(_infoOption, _infoAlias)
        {
            Description = Resources.InfoDescription,
            Action = new SystemInfoAction(),
        };

        var port = new Option<int?>(_portOption, _portAlias);
        var tls = new Option<bool>(_tlsOption, _tlsAlias);
        var tlsCertificate = new Option<FileInfo>(_tlsCertificateOption, _certificateAlias);
        tlsCertificate.AcceptExistingOnly();
        var tlsTrustAll = new Option<bool>(_tlsTrustAllOption);
        var tlsTrustAllShortcut = new Option<bool>(_tlsTrustAllAlias);
        var wide = new Option<bool>(_wideOption, _wideAlias);
        var device = new Option<string>(_deviceOption, _deviceAlias)
        {
            DefaultValueFactory = _ => DefaultDevice(Environment.MachineName),
        };

        var codePage = new Option<int>(_codePageOption, _codePageAlias)
        {
            DefaultValueFactory = _ => _defaultCodePage,
        };

        var keyboard = new Option<string>(_keyboardOption, _keyboardAlias)
        {
            DefaultValueFactory = _ => _defaultKeyboard,
        };

        var recordFile = new Option<string?>(_recordOption, _recordAlias);
        var replayFile = new Option<FileInfo>(_replayOption, _replayAlias);
        replayFile.AcceptExistingOnly();
        var replaySpeed = new Option<double>(_replaySpeedOption, _replaySpeedAlias)
        {
            DefaultValueFactory = _ => _fullReplayTiming,
            CustomParser = ParseTiming,
        };

        var replayTimeout = new Option<double>(_replayTimeoutOption, _replayTimeoutAlias)
        {
            DefaultValueFactory = _ => _defaultReplayTimeout,
            CustomParser = ParseTiming,
        };

        var replayUsers = new Option<int>(_usersOption, _usersAlias)
        {
            DefaultValueFactory = _ => 1,
            Description = Resources.ReplayUsersDescription,
        };

        var userInterval = new Option<double>(_userIntervalOption, _userIntervalAlias)
        {
            CustomParser = ParseTiming,
            Description = Resources.UserIntervalDescription,
        };

        var userNumber = new Option<int>(_userNumberOption)
        {
            DefaultValueFactory = _ => 1,
            Description = Resources.ReplayUserNumberDescription,
        };

        var replayUserLimit = new Option<int>(_replayUserLimitOption)
        {
            Hidden = true,
        };

        var replayGroup = new Option<string?>(_replayGroupOption);
        var captureDirectory = new Option<string?>(_captureDirectoryOption, _captureAlias);
        var command = new RootCommand(Resources.CommandDescription);
        var help = command.Options.OfType<HelpOption>().Single();
        help.Action = new BannerHelpAction((HelpAction)help.Action!);
        command.Arguments.Add(host);
        command.Options.Add(info);
        command.Options.Add(port);
        command.Options.Add(tls);
        command.Options.Add(tlsCertificate);
        command.Options.Add(tlsTrustAll);
        command.Options.Add(tlsTrustAllShortcut);
        command.Options.Add(wide);
        command.Options.Add(device);
        command.Options.Add(codePage);
        command.Options.Add(keyboard);
        command.Options.Add(captureDirectory);
        command.Options.Add(recordFile);
        command.Options.Add(replayFile);
        command.Options.Add(replaySpeed);
        command.Options.Add(replayTimeout);
        command.Options.Add(replayGroup);
        command.Options.Add(userNumber);
        command.Options.Add(replayUserLimit);
        command.Options.Add(replayUsers);
        command.Options.Add(userInterval);
        host.Description = Resources.HostDescription;
        port.Description = Resources.PortDescription;
        tls.Description = Resources.TlsDescription;
        tlsCertificate.Description = Resources.TlsCertificateDescription;
        tlsTrustAll.Description = Resources.TlsTrustAllDescription;
        tlsTrustAllShortcut.Description = Resources.TlsTrustAllShortcutDescription;
        wide.Description = Resources.WideDescription;
        device.Description = Resources.DeviceDescription;
        codePage.Description = Resources.CodePageDescription;
        keyboard.Description = Resources.KeyboardDescription;
        captureDirectory.Description = Resources.CaptureDescription;
        recordFile.Description = Resources.RecordDescription;
        replayFile.Description = Resources.ReplayDescription;
        replaySpeed.Description = Resources.ReplaySpeedDescription;
        replayTimeout.Description = Resources.ReplayTimeoutDescription;
        replayGroup.Hidden = true;
        var parsed = command.Parse(args.Length == 0 ? [_helpOption] : args);
        if (parsed.Errors.Count != 0)
            throw new ArgumentException(string.Join(Environment.NewLine, parsed.Errors.Select(error => error.Message)));

        if (parsed.Action is BannerHelpAction or SystemInfoAction || parsed.GetResult(_versionOption) != null)
        {
            return new Options
            {
                Help = parsed.Action is BannerHelpAction,
                Version = parsed.GetResult(_versionOption) != null,
                Invocation = parsed,
            };
        }

        var options = new Options
        {
            CaptureDirectory = parsed.GetValue(captureDirectory),
            RecordFile = parsed.GetValue(recordFile),
            ReplayFile = parsed.GetValue(replayFile)?.FullName,
            ReplaySpeed = parsed.GetValue(replaySpeed),
            ReplayUsers = parsed.GetValue(replayUsers),
            ReplayUserNumber = parsed.GetValue(userNumber),
            ReplayUserLimit = parsed.GetValue(replayUserLimit),
            UserInterval = parsed.GetValue(userInterval),
            ReplayTimeout = parsed.GetValue(replayTimeout),
            ReplayGroup = parsed.GetValue(replayGroup),
            Host = parsed.GetValue(host) ?? string.Empty,
            Tls = parsed.GetValue(tls) || parsed.GetValue(tlsTrustAllShortcut),
            TlsCertificate = parsed.GetValue(tlsCertificate),
            TlsTrustAll = parsed.GetValue(tlsTrustAll) || parsed.GetValue(tlsTrustAllShortcut),
            Wide = parsed.GetValue(wide),
            Device = parsed.GetValue(device) ?? string.Empty,
            DeviceSpecified = parsed.GetResult(device) is { Implicit: false },
            CodePage = parsed.GetValue(codePage),
            Keyboard = parsed.GetValue(keyboard) ?? _defaultKeyboard,
        };

        if (string.IsNullOrWhiteSpace(options.Host))
            throw new ArgumentException(Resources.HostRequired);

        if (options.CaptureDirectory != null && string.IsNullOrWhiteSpace(options.CaptureDirectory))
            throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, Resources.InvalidValue,
                _captureDirectoryOption, options.CaptureDirectory));

        if (options.RecordFile != null && (string.IsNullOrWhiteSpace(options.RecordFile) || options.ReplayFile != null))
            throw new ArgumentException(Resources.RecordingOptions);

        if (!double.IsFinite(options.ReplaySpeed) || options.ReplaySpeed < 0 || options.ReplaySpeed > _fullReplayTiming
            || !double.IsFinite(options.ReplayTimeout)
            || options.ReplayTimeout <= 0 || options.ReplayTimeout > _maximumReplayTimeout)
            throw new ArgumentException(Resources.ReplayTimingOptions);

        if (options.ReplayUsers < 1 || !double.IsFinite(options.UserInterval)
            || options.UserInterval < 0 || options.UserInterval > _maximumReplayTimeout)
            throw new ArgumentException(Resources.ReplayUsersOptions);

        var lastUser = (long)options.ReplayUserNumber + options.ReplayUsers - 1;
        if (options.ReplayUserNumber < 1 || lastUser > ReplayDeviceNames._maximumNumber
            || options.ReplayUserLimit < 0 || options.ReplayUserLimit > ReplayDeviceNames._maximumNumber
            || options.ReplayUserLimit > 0 && options.ReplayUserLimit < lastUser)
            throw new ArgumentException(Resources.ReplayUserNumberOptions);

        var hasReplayTiming = parsed.GetResult(replaySpeed) is { Implicit: false }
            || parsed.GetResult(replayTimeout) is { Implicit: false }
            || parsed.GetResult(replayUsers) is { Implicit: false }
            || parsed.GetResult(userInterval) is { Implicit: false }
            || parsed.GetResult(userNumber) is { Implicit: false }
            || parsed.GetResult(replayUserLimit) is { Implicit: false };
        if (options.ReplayFile == null && hasReplayTiming)
            throw new ArgumentException(Resources.ReplayOptions);

        if (options.ReplayGroup != null && (options.ReplayFile == null || !Guid.TryParseExact(options.ReplayGroup, "N", out _)))
            throw new ArgumentException(Resources.ReplayOptions);

        if (!options.Tls && (options.TlsCertificate != null || options.TlsTrustAll))
            throw new ArgumentException(Resources.TlsOptionsRequireTls);

        if (options.TlsCertificate != null && options.TlsTrustAll)
            throw new ArgumentException(Resources.TlsOptionsExclusive);

        options.Port = parsed.GetValue(port) ?? (options.Tls ? _tlsPort : _telnetPort);
        ValidateNumber(options.Port, _portOption);
        ValidateNumber(options.CodePage, _codePageOption);
        options.Device = ValidateName(options.Device, _deviceOption, false);
        options.Keyboard = ValidateName(options.Keyboard, _keyboardOption, true);
        return options;
    }

    internal static string DefaultDevice(string machineName)
    {
        var name = new string(machineName.ToUpperInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) || character is '$' or '#' or '@' ? character : '_').ToArray());
        if (name.Length == 0 || !char.IsAsciiLetter(name[0]) && name[0] is not '$' and not '#' and not '@')
        {
            name = _devicePrefix + name;
        }

        return name[..Math.Min(name.Length, _maximumNameLength)];
    }

    private static double ParseTiming(ArgumentResult result)
    {
        if (result.Tokens.Count == 1 && double.TryParse(result.Tokens[0].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return value;

        result.AddError(Resources.ReplayTimingOptions);
        return 0;
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
