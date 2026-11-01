namespace Terminal5250;

internal sealed class SessionReplay
{
    private readonly List<RecordedAction> _actions;
    private readonly double _speed;
    private readonly double _timeout;
    private int _index;
    private long _previous;
    private long? _pausedAt;
    private long _screenRevision;
    private bool _waitingForScreen = true;

    internal string FilePath { get; }
    internal bool IsComplete => _index == _actions.Count;
    internal string Status => string.Format(CultureInfo.CurrentCulture, Resources.ReplayStatus,
        Math.Min(_index + 1, _actions.Count), _actions.Count, Path.GetFileName(FilePath), NextKey());

    internal SessionReplay(string path, Options options)
    {
        FilePath = Path.GetFullPath(path);
        _actions = SessionRecorder.Read(path, options);
        _speed = options.ReplaySpeed;
        _timeout = options.ReplayTimeout * 1000;
    }

    internal void SetPaused(bool paused, long now)
    {
        if (paused && _pausedAt == null)
        {
            _pausedAt = now;
        }
        else if (!paused && _pausedAt is { } started)
        {
            _previous += now - started;
            _pausedAt = null;
        }
    }

    internal RecordedAction? Next(Display display, long now)
    {
        if (IsComplete || _pausedAt != null)
            return null;

        var action = _actions[_index];
        var delay = action.Delay * (_speed / Options._fullReplayTiming);
        if (!double.IsFinite(delay))
            throw new InvalidDataException(Resources.ReplayTimingOptions);

        if (now - _previous < delay)
            return null;

        var finishing = action.Key == null || ConsoleInput.IsQuit(action.Key.Value);
        var ready = !display.Locked && (display.ReadPending || display.InputError || finishing);
        if (ready && (!_waitingForScreen || display.ScreenRevision > _screenRevision))
        {
            _waitingForScreen = false;
            return action;
        }

        if (now - _previous - delay > _timeout)
            throw new TimeoutException(string.Format(CultureInfo.CurrentCulture, Resources.ReplayTimeout, _index + 1));

        return null;
    }

    internal bool FinishOnDisconnect()
    {
        if (_actions.Count - _index != 1 || _actions[_index].Key != null)
            return false;

        _index++;
        return true;
    }

    internal void Advance(RecordedAction action, byte[]? reply, Display display, long now)
    {
        if (action.Submitted != (reply != null) || action.Key != null && !ConsoleInput.IsQuit(action.Key.Value)
            && display.InputError != action.ResultError)
            throw new InvalidDataException(string.Format(CultureInfo.CurrentCulture, Resources.ReplayFailed, _index + 1));

        if (reply != null)
        {
            _screenRevision = display.ScreenRevision;
            _waitingForScreen = true;
        }

        _index++;
        _previous = now;
    }

    private string NextKey()
    {
        if (IsComplete || _actions[_index].Key == null)
            return Resources.ReplayFinishing;

        var key = _actions[_index].Key!.Value;
        return !char.IsControl(key.KeyChar) ? Resources.ReplayTyping : HtmlScreenCapture.DescribeAction(key);
    }
}
