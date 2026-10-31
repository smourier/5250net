namespace Terminal5250;

internal sealed class SessionUsers : IAsyncDisposable
{
    private const string _identifierFormat = "N";
    private const string _directoryName = "5250net-replays";
    private const string _sessionExtension = ".session";
    private const string _sessionPattern = "*.session";
    private const string _snapshotPattern = "*.replay";
    private const string _pipePrefix = "5250net-";
    private const string _terminalVariable = "TERMINAL";
    private const string _windowsTerminal = "wt.exe";
    private const string _windowsTerminalSession = "WT_SESSION";
    private const string _commandPrompt = "cmd.exe";
    private const string _commandScriptExtension = ".cmd";
    private const string _commandPrefix = "/d /v:off /s /c \"\"";
    private const string _commandSuffix = "\"\"";
    private const string _quote = "\"";
    private const string _percent = "%";
    private const string _escapedPercent = "%%";
    private const string _lineEnding = "\r\n";
    private const string _commandScriptPrefix = "@echo off\r\nchcp 65001 >nul\r\nsetlocal DisableDelayedExpansion\r\n";

    private const string _commandScriptSuffix = "exit /b %errorlevel%\r\n";

    private const string _newTab = "new-tab";
    private const string _execute = "-e";
    private const string _separator = "--";
    private const int _pipeTimeout = 1500;
    private const int _noticeMilliseconds = 3000;
    private const int _maximumPathLength = 2048;
    private const int _launchChecks = 100;
    private const int _launchPollMilliseconds = 10;
    private const byte _stop = 1;
    private static readonly string[] _terminals =
        ["xdg-terminal-exec", "alacritty", "kitty", "ghostty", "gnome-terminal", "konsole", "foot", "xterm"];

    private static readonly byte[] _stopMessage = [_stop];
    private static readonly string[] _removedOptions =
    [
        Options._recordOption, Options._recordAlias, Options._replayOption, Options._replayAlias,
        Options._replayGroupOption, Options._deviceOption, Options._deviceAlias,
        Options._usersOption, Options._usersAlias, Options._userIntervalOption, Options._userIntervalAlias,
    ];

    private readonly string[] _arguments;
    private readonly Options _optionsForReplay;
    private readonly CancellationTokenSource _sessionCancellation;
    private readonly CancellationTokenSource _listening = new();
    private readonly NamedPipeServerStream _pipe;
    private readonly Task _listener;
    private readonly string _id = Guid.NewGuid().ToString(_identifierFormat);
    private readonly string _group;
    private readonly string _registration;
    private readonly string _rootDirectory;
    private List<ReplayUser>? _selection;
    private int _selected;
    private string? _notice;
    private bool _keepSnapshots;

    internal string DirectoryPath { get; }
    private string? _sourcePrompt;
    private long _noticeUntil;

    internal bool IsSelecting => _selection != null || _sourcePrompt != null;
    internal string? Status
    {
        get
        {
            if (_sourcePrompt != null)
                return string.Format(CultureInfo.CurrentCulture, Resources.ReplaySourcePrompt, _sourcePrompt);

            if (_selection is { Count: > 0 })
                return string.Format(CultureInfo.CurrentCulture, Resources.ReplayUserStatus,
                    _selected + 1, _selection.Count, _selection[_selected].ProcessId);

            return Environment.TickCount64 < _noticeUntil ? _notice : null;
        }
    }

    internal SessionUsers(Options options, string[] arguments, CancellationTokenSource cancellation, string? rootDirectory = null)
    {
        _arguments = arguments;
        _optionsForReplay = options;
        _sessionCancellation = cancellation;
        _group = options.ReplayGroup ?? Guid.NewGuid().ToString(_identifierFormat);
        _rootDirectory = rootDirectory ?? Path.Combine(Path.GetTempPath(), _directoryName);
        DirectoryPath = Path.Combine(_rootDirectory, _group);
        Directory.CreateDirectory(DirectoryPath);
        if (options.ReplayFile != null && !options.DeviceSpecified)
        {
            options.Device = ReplayDeviceNames.Next(_rootDirectory);
        }

        _registration = Path.Combine(DirectoryPath, _id + _sessionExtension);
        var pipeName = _pipePrefix + _group + _id;
        _pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var process = Process.GetCurrentProcess();
        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        var started = process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
        File.WriteAllLines(_registration, [pid, started]);
        _listener = ListenAsync();
    }

    private async Task ListenAsync()
    {
        try
        {
            while (!_listening.IsCancellationRequested)
            {
                await _pipe.WaitForConnectionAsync(_listening.Token);
                var command = new byte[1];
                var count = await _pipe.ReadAsync(command, _listening.Token);
                _pipe.Disconnect();
                if (count == 1 && command[0] == _stop)
                {
                    File.Delete(_registration);
                    _sessionCancellation.Cancel();
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
    }

    internal List<ReplayUser> GetUsers(bool all = false)
    {
        var users = new List<ReplayUser>();
        var directories = all ? Directory.EnumerateDirectories(_rootDirectory) : [DirectoryPath];
        foreach (var file in directories.SelectMany(directory => Directory.EnumerateFiles(directory, _sessionPattern)))
        {
            try
            {
                var id = Path.GetFileNameWithoutExtension(file);
                var group = Path.GetFileName(Path.GetDirectoryName(file));
                if (!Guid.TryParseExact(id, _identifierFormat, out _) || !Guid.TryParseExact(group, _identifierFormat, out _))
                    continue;

                var lines = File.ReadAllLines(file);
                if (lines.Length != 2 || !int.TryParse(lines[0], CultureInfo.InvariantCulture, out var pid)
                    || !long.TryParse(lines[1], CultureInfo.InvariantCulture, out var ticks))
                    continue;

                using var process = Process.GetProcessById(pid);
                if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks == ticks)
                {
                    users.Add(new ReplayUser(id, pid, _pipePrefix + group + id));
                }
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException or Win32Exception)
            {
            }
        }

        return [.. users.OrderBy(user => user.ProcessId).ThenBy(user => user.Id, StringComparer.Ordinal)];
    }

    internal static async Task StopAsync(ReplayUser user, CancellationToken cancellation)
    {
        using var pipe = new NamedPipeClientStream(".", user.PipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(_pipeTimeout, cancellation);
        await pipe.WriteAsync(_stopMessage, cancellation);
    }

    internal async Task<bool> HandleAsync(ConsoleKeyInfo key, Display display, SessionRecorder? recorder, SessionReplay? replay, long now)
    {
        var add = key.Modifiers == ConsoleModifiers.Control && key.Key == ConsoleKey.N;
        var choose = key.Modifiers == ConsoleModifiers.Control && key.Key == ConsoleKey.K;
        var stopOthers = key.Modifiers == ConsoleModifiers.Control && key.Key == ConsoleKey.L;
        if (!add && !choose && !stopOthers && !IsSelecting)
            return false;

        try
        {
            if (_sourcePrompt != null && !choose && !stopOthers)
            {
                if (key.Key == ConsoleKey.Escape)
                {
                    _sourcePrompt = null;
                }
                else if (key.Key == ConsoleKey.Enter)
                {
                    var source = Path.GetFullPath(_sourcePrompt);
                    _sourcePrompt = null;
                    SessionRecorder.Read(source, _optionsForReplay);
                    await LaunchAsync(source);
                    Notice(Resources.ReplayUserAdded);
                }
                else if (key.Key == ConsoleKey.Backspace && _sourcePrompt.Length > 0)
                {
                    _sourcePrompt = _sourcePrompt[..^1];
                }
                else if (!char.IsControl(key.KeyChar) && _sourcePrompt.Length < _maximumPathLength)
                {
                    _sourcePrompt += key.KeyChar;
                }
            }
            else if (add)
            {
                var source = replay?.FilePath ?? recorder?.Snapshot(DirectoryPath, display, now);
                if (source == null)
                {
                    _sourcePrompt = string.Empty;
                }
                else
                {
                    await LaunchAsync(source);
                    Notice(Resources.ReplayUserAdded);
                }
            }
            else if (stopOthers)
            {
                foreach (var user in GetUsers(true).Where(user => user.Id != _id))
                {
                    try
                    {
                        await StopAsync(user, _listening.Token);
                    }
                    catch (Exception exception) when (exception is IOException or TimeoutException)
                    {
                    }
                }

                _selection = null;
                Notice(Resources.ReplayUsersStopped);
            }
            else if (choose)
            {
                _selection = [.. GetUsers(true).Where(user => user.Id != _id)];
                _selected = 0;
                if (_selection.Count == 0)
                {
                    _selection = null;
                    Notice(Resources.ReplayNoUsers);
                }
            }
            else if (_selection != null)
            {
                if (key.Key == ConsoleKey.Escape)
                {
                    _selection = null;
                    _notice = null;
                }
                else if (key.Key is ConsoleKey.LeftArrow or ConsoleKey.UpArrow)
                {
                    _selected = (_selected + _selection.Count - 1) % _selection.Count;
                }
                else if (key.Key is ConsoleKey.RightArrow or ConsoleKey.DownArrow)
                {
                    _selected = (_selected + 1) % _selection.Count;
                }
                else if (key.Key == ConsoleKey.Enter)
                {
                    var user = _selection[_selected];
                    await StopAsync(user, _listening.Token);
                    _selection = null;
                    Notice(string.Format(CultureInfo.CurrentCulture, Resources.ReplayUserStopped, user.ProcessId));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or Win32Exception
            or TimeoutException or ArgumentException)
        {
            _selection = null;
            _sourcePrompt = null;
            Notice(string.Format(CultureInfo.CurrentCulture, Resources.ReplayUserError, exception.Message));
        }

        return true;
    }

    private void Notice(string message)
    {
        _notice = message;
        _noticeUntil = Environment.TickCount64 + _noticeMilliseconds;
    }

    internal async Task LaunchUsersAsync(CancellationToken cancellation)
    {
        for (var user = 1; user < _optionsForReplay.ReplayUsers; user++)
        {
            await Task.Delay(TimeSpan.FromSeconds(_optionsForReplay.UserInterval), cancellation);
            cancellation.ThrowIfCancellationRequested();
            Launch(_optionsForReplay.ReplayFile!);
        }
    }

    private async Task LaunchAsync(string source)
    {
        var existing = GetUsers().Select(user => user.Id).ToHashSet(StringComparer.Ordinal);
        Launch(source);
        for (var check = 0; check < _launchChecks; check++)
        {
            if (GetUsers().Any(user => !existing.Contains(user.Id)))
                return;

            await Task.Delay(_launchPollMilliseconds, _listening.Token);
        }

        _keepSnapshots |= Path.GetDirectoryName(source) == DirectoryPath;
    }

    private void Launch(string source)
    {
        var arguments = ReplayArguments(source);
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException(Resources.ReplayNoTerminal);
        if (OperatingSystem.IsWindows())
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(_windowsTerminalSession)))
            {
                try
                {
                    Start(_windowsTerminal, [_newTab, executable, .. arguments]);
                    return;
                }
                catch (Win32Exception)
                {
                }
            }

            var info = CreateCommandPrompt(executable, arguments, DirectoryPath);
            var commandPrompt = Process.Start(info) ?? throw new InvalidOperationException(Resources.ReplayNoTerminal);

            // the launcher must remain on disk until cmd has finished reading it.
            _ = CleanupCommandPromptAsync(commandPrompt, info);
            return;
        }

        var configured = Environment.GetEnvironmentVariable(_terminalVariable);
        var terminals = new List<string>(_terminals);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            terminals.Insert(0, configured);
        }

        foreach (var terminal in terminals)
        {
            try
            {
                var separator = terminal is "xdg-terminal-exec" or "gnome-terminal" or "kitty" ? _separator : _execute;
                Start(terminal, [separator, executable, .. arguments]);
                return;
            }
            catch (Win32Exception)
            {
            }
        }

        throw new InvalidOperationException(Resources.ReplayNoTerminal);
    }

    internal static ProcessStartInfo CreateCommandPrompt(string executable, IEnumerable<string> arguments, string directory)
    {
        var launcher = Path.Combine(directory, Guid.NewGuid().ToString(_identifierFormat) + _commandScriptExtension);
        if (launcher.Contains('%'))
            throw new ArgumentException(Resources.ReplayCommandPromptArguments);

        string[] commandArguments = [executable, .. arguments];
        var command = string.Join(' ', commandArguments.Select(QuoteCommandPromptArgument));
        var script = _commandScriptPrefix + command + _lineEnding + _commandScriptSuffix.ReplaceLineEndings(_lineEnding) + _lineEnding;
        File.WriteAllText(launcher, script, new UTF8Encoding(false));
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), _commandPrompt);
        return new ProcessStartInfo(shell)
        {
            UseShellExecute = true,
            WorkingDirectory = Environment.CurrentDirectory,
            Arguments = _commandPrefix + launcher + _commandSuffix,
        };
    }

    internal static async Task CleanupCommandPromptAsync(Process process, ProcessStartInfo info)
    {
        using (process)
        {
            try
            {
                await process.WaitForExitAsync();
                var launcher = info.Arguments[_commandPrefix.Length..^_commandSuffix.Length];
                File.Delete(launcher);
                Directory.Delete(Path.GetDirectoryName(launcher)!);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or Win32Exception
                or UnauthorizedAccessException)
            {
            }
        }
    }

    internal static string QuoteCommandPromptArgument(string argument)
    {
        if (argument.IndexOfAny(['"', '\r', '\n']) >= 0)
            throw new ArgumentException(Resources.ReplayCommandPromptArguments);

        var trailing = argument.Length - argument.TrimEnd('\\').Length;
        return _quote + argument.Replace(_percent, _escapedPercent, StringComparison.Ordinal) + new string('\\', trailing) + _quote;
    }

    internal string[] ReplayArguments(string source)
    {
        var arguments = new List<string>();
        for (var i = 0; i < _arguments.Length; i++)
        {
            var argument = _arguments[i];
            if (argument == _separator)
                continue;

            if (IsRecordingOption(argument))
            {
                if (!argument.Contains('=') && i + 1 < _arguments.Length)
                {
                    i++;
                }

                continue;
            }

            arguments.Add(argument);
        }

        arguments.AddRange([Options._replayOption, Path.GetFullPath(source), Options._replayGroupOption, _group]);
        return [.. arguments];
    }

    private static bool IsRecordingOption(string argument)
    {
        return _removedOptions.Any(option => argument == option || argument.StartsWith(option + "=", StringComparison.Ordinal));
    }

    private static void Start(string executable, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException(Resources.ReplayNoTerminal);
    }

    public async ValueTask DisposeAsync()
    {
        _listening.Cancel();
        _pipe.Dispose();
        await _listener;
        _listening.Dispose();
        try
        {
            File.Delete(_registration);
            if (GetUsers().Count == 0 && !_keepSnapshots)
            {
                foreach (var snapshot in Directory.EnumerateFiles(DirectoryPath, _snapshotPattern))
                {
                    File.Delete(snapshot);
                }

                Directory.Delete(DirectoryPath);
            }
        }
        catch (IOException)
        {
        }
    }
}
