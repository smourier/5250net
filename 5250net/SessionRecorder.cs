namespace Terminal5250;

internal sealed class SessionRecorder : IDisposable
{
    private const int _version = 2;
    private const int _legacyVersion = 1;
    private const string _identifierFormat = "N";
    private const int _allModifiers = 7;
    private const int _maximumActions = 1000000;
    private const long _maximumFileLength = 67108864;
    private const long _maximumDelay = 604800000;
    private const string _type = "type";
    private const string _header = "header";
    private const string _key = "key";
    private const string _end = "end";
    private const string _versionName = "version";
    private const string _codePage = "codePage";
    private const string _wide = "wide";
    private const string _keyboard = "keyboard";
    private const string _delay = "delayMs";
    private const string _reply = "reply";
    private const string _submitted = "submitted";
    private const string _resultError = "resultError";
    private const string _character = "character";
    private const string _modifiers = "modifiers";
    private const string _snapshotExtension = ".replay";
    private static readonly byte[] _lineEnding = [13, 10];
    private readonly FileStream _stream;
    private long _previous;
    private int _count;
    private bool _completed;

    internal string FilePath => _stream.Name;

    internal SessionRecorder(string path, Options options)
    {
        _stream = Create(path);
        Write(_stream, writer =>
        {
            writer.WriteString(_type, _header);
            writer.WriteNumber(_versionName, _version);
            writer.WriteNumber(_codePage, options.CodePage);
            writer.WriteBoolean(_wide, options.Wide);
            writer.WriteString(_keyboard, options.Keyboard);
        });
    }

    internal void Record(ConsoleKeyInfo key, byte[]? reply, bool resultError, long now)
    {
        if (_count >= _maximumActions - 1)
            throw new InvalidDataException(Resources.RecordingLimit);

        WriteAction(_stream, new RecordedAction(key, now - _previous, reply != null, resultError));
        _previous = now;
        _count++;
    }

    internal void Complete(Display display, long now)
    {
        if (_completed)
            return;

        WriteAction(_stream, new RecordedAction(null, now - _previous, false, display.InputError));
        _completed = true;
    }

    internal string Snapshot(string directory, Display display, long now)
    {
        var path = Path.Combine(directory, Guid.NewGuid().ToString(_identifierFormat) + _snapshotExtension);
        _stream.Flush();
        using var input = new FileStream(_stream.Name, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var output = Create(path);
        input.CopyTo(output);
        WriteAction(output, new RecordedAction(null, now - _previous, false, display.InputError));
        return path;
    }

    internal static List<RecordedAction> Read(string path, Options options)
    {
        if (new FileInfo(path).Length > _maximumFileLength)
            throw new InvalidDataException(Resources.RecordingInvalid);

        try
        {
            var actions = new List<RecordedAction>();
            var version = 0;
            var ended = false;
            foreach (var line in File.ReadLines(path))
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var type = root.GetProperty(_type).GetString();
                if (version == 0)
                {
                    version = root.GetProperty(_versionName).GetInt32();
                    if (type != _header || version is not (_legacyVersion or _version))
                        throw new InvalidDataException(Resources.RecordingInvalid);

                    if (root.GetProperty(_codePage).GetInt32() != options.CodePage || root.GetProperty(_wide).GetBoolean() != options.Wide
                        || root.GetProperty(_keyboard).GetString() != options.Keyboard)
                        throw new InvalidDataException(Resources.RecordingConfiguration);

                    continue;
                }

                if (ended || type is not (_key or _end) || actions.Count >= _maximumActions)
                    throw new InvalidDataException(Resources.RecordingInvalid);

                ConsoleKeyInfo? key = null;
                if (type == _key)
                {
                    var character = root.GetProperty(_character).GetInt32();
                    var code = root.GetProperty(_key).GetInt32();
                    var modifiers = root.GetProperty(_modifiers).GetInt32();
                    if (character is < char.MinValue or > char.MaxValue || code != 0 && !Enum.IsDefined((ConsoleKey)code)
                        || modifiers is < 0 or > _allModifiers)
                        throw new InvalidDataException(Resources.RecordingInvalid);

                    var flags = (ConsoleModifiers)modifiers;
                    key = new ConsoleKeyInfo((char)character, (ConsoleKey)code, flags.HasFlag(ConsoleModifiers.Shift),
                        flags.HasFlag(ConsoleModifiers.Alt), flags.HasFlag(ConsoleModifiers.Control));
                }

                var delay = root.GetProperty(_delay).GetInt64();
                if (delay is < 0 or > _maximumDelay)
                    throw new InvalidDataException(Resources.RecordingInvalid);

                var submitted = version == _legacyVersion
                    ? root.GetProperty(_reply).GetString() != null : root.GetProperty(_submitted).GetBoolean();
                if (key == null && submitted)
                    throw new InvalidDataException(Resources.RecordingInvalid);

                actions.Add(new RecordedAction(key, delay, submitted, root.GetProperty(_resultError).GetBoolean()));
                ended = type == _end;
            }

            if (version == 0 || !ended)
                throw new InvalidDataException(Resources.RecordingInvalid);

            return actions;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException(Resources.RecordingInvalid, exception);
        }
    }

    private static FileStream Create(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.Read,
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return new FileStream(path, options);
    }

    private static void WriteAction(Stream stream, RecordedAction action)
    {
        Write(stream, writer =>
        {
            writer.WriteString(_type, action.Key == null ? _end : _key);
            writer.WriteNumber(_delay, action.Delay);
            writer.WriteBoolean(_submitted, action.Submitted);
            writer.WriteBoolean(_resultError, action.ResultError);
            if (action.Key is { } key)
            {
                writer.WriteNumber(_key, (int)key.Key);
                writer.WriteNumber(_character, key.KeyChar);
                writer.WriteNumber(_modifiers, (int)key.Modifiers);
            }
        });
    }

    private static void Write(Stream stream, Action<Utf8JsonWriter> append)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            append(writer);
            writer.WriteEndObject();
        }

        if (stream.Position + buffer.Length + _lineEnding.Length > _maximumFileLength)
            throw new InvalidDataException(Resources.RecordingLimit);

        buffer.Position = 0;
        buffer.CopyTo(stream);
        stream.Write(_lineEnding);
        stream.Flush();
    }

    public void Dispose() => _stream.Dispose();
}
