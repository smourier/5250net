namespace Terminal5250;

internal sealed class ConsoleRenderer : IDisposable
{
    private const char _buildMetadataSeparator = '+';
    private const byte _normalAttribute = 0x20;
    private const byte _attributeMask = 0xE0;
    private const byte _nonDisplayMask = 7;
    private const byte _underlineMask = 4;
    private const string _resetStyle = "\u001b[0m";
    private const string _stylePrefix = "\u001b[0;";
    private const string _underlineStyle = ";4";
    private const int _blackForeground = 30;
    private const int _blackBackground = 40;
    private const int _backgroundOffset = 10;
    private static readonly int[] _foregroundCodes = [32, 97, 31, 31, 36, 33, 35, 34];

    private const string _terminalVariable = "TERM";
    private const string _dumbTerminal = "dumb";
    private readonly bool _ansi = !OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable(_terminalVariable) != _dumbTerminal;
    private readonly string _host;
    private readonly ConsoleColor _foreground;
    private readonly ConsoleColor _background;
    private readonly Encoding _encoding;
    private readonly string? _title;
    private const string _saveTitle = "\u001b[22;0t";
    private const string _restoreTitle = "\u001b[23;0t";
    private int _width;
    private int _height;
    private static readonly ConsoleColor[] _colors =
    [
        ConsoleColor.Green, ConsoleColor.White, ConsoleColor.Red, ConsoleColor.Red,
        ConsoleColor.Cyan, ConsoleColor.Yellow, ConsoleColor.Magenta, ConsoleColor.Blue,
    ];

    internal ConsoleRenderer(string host, int? userNumber = null)
    {
        _host = host;
        _foreground = Console.ForegroundColor;
        _background = Console.BackgroundColor;
        _encoding = Console.OutputEncoding;
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.Clear();
        var title = BuildTitle(host, userNumber);
        if (OperatingSystem.IsWindows())
        {
            _title = Console.Title;
            Console.Title = title;
        }
        else if (_ansi)
        {
            Console.Write(_saveTitle);
            Console.Title = title;
        }
    }

    internal static string BuildTitle(string host, int? userNumber = null)
    {
        var title = string.Format(CultureInfo.InvariantCulture, Resources.ConsoleTitle, host);
        return userNumber.HasValue
            ? string.Format(CultureInfo.InvariantCulture, Resources.ReplayConsoleTitle, title, userNumber.Value) : title;
    }

    internal bool Resized() => Console.WindowWidth != _width || Console.WindowHeight != _height;

    internal static bool Fits(Display display) => Console.WindowWidth >= display.Columns && Console.WindowHeight > display.Rows;

    internal static string BuildBanner(int year)
    {
        var assembly = typeof(ConsoleRenderer).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3) ?? Resources.HtmlUnknown;
        var metadata = version.IndexOf(_buildMetadataSeparator);
        if (metadata >= 0)
        {
            version = version[..metadata];
        }

        return string.Format(CultureInfo.InvariantCulture, Resources.Banner, version, year);
    }

    internal void Draw(Display display, string? activity = null)
    {
        var resized = Resized();
        _width = Console.WindowWidth;
        _height = Console.WindowHeight;
        Console.CursorVisible = false;
        if (resized)
        {
            Console.Clear();
        }

        if (!Fits(display))
        {
            Console.ResetColor();
            Console.SetCursorPosition(0, 0);
            var message = string.Format(CultureInfo.CurrentCulture, Resources.TooSmall, display.Columns, display.Rows + 1);
            Console.Write(message.AsSpan(0, Math.Min(message.Length, Math.Max(0, _width - 1))));
            return;
        }

        byte attribute = _normalAttribute;
        for (var row = 0; row < display.Rows; row++)
        {
            Console.SetCursorPosition(0, row);
            if (_ansi)
            {
                Console.Write(BuildRow(display, row, true));
                continue;
            }

            var text = new StringBuilder();
            SetColors(attribute);
            for (var column = 0; column < display.Columns; column++)
            {
                var value = display.Data[row * display.Columns + column];
                if ((value & _attributeMask) == _normalAttribute)
                {
                    Console.Write(text.ToString());
                    text.Clear();
                    attribute = value;
                    SetColors(attribute);
                    text.Append(' ');
                }
                else
                {
                    text.Append(VisibleCharacter(display, value, attribute, true));
                }
            }

            Console.Write(text.ToString());
        }

        Console.ResetColor();
        Console.SetCursorPosition(0, display.Rows);
        var state = display.InputError ? Resources.InputError : display.Locked ? Resources.Locked : Resources.Ready;
        if (display.MessageWaiting)
        {
            state += Resources.MessageWaiting;
        }

        if (display.InsertMode)
        {
            state += Resources.InsertMode;
        }

        var status = string.Format(CultureInfo.CurrentCulture, Resources.Status,
            _host, display.Rows, display.Columns, display.Cursor / display.Columns + 1, display.Cursor % display.Columns + 1, state);
        status = activity == null ? status : string.Format(CultureInfo.CurrentCulture, Resources.ActivityStatus, state, activity);
        var length = Math.Max(0, _width - 1);
        Console.Write(status.Length > length ? status[..length] : status.PadRight(length));
        Console.SetCursorPosition(display.Cursor % display.Columns, display.Cursor / display.Columns);
        Console.CursorVisible = !display.Locked;
    }

    internal static string BuildRow(Display display, int row, bool ansi)
    {
        var builder = new StringBuilder();
        var start = row * display.Columns;
        var attribute = _normalAttribute;
        for (var i = 0; i < start; i++)
        {
            if ((display.Data[i] & _attributeMask) == _normalAttribute)
            {
                attribute = display.Data[i];
            }
        }

        if (ansi)
        {
            AppendStyle(builder, attribute);
        }

        for (var i = start; i < start + display.Columns; i++)
        {
            var value = display.Data[i];
            if ((value & _attributeMask) == _normalAttribute)
            {
                builder.Append(' ');
                attribute = value;
                if (ansi)
                {
                    AppendStyle(builder, attribute);
                }
            }
            else
            {
                builder.Append(VisibleCharacter(display, value, attribute, !ansi));
            }
        }

        if (ansi)
        {
            builder.Append(_resetStyle);
        }

        return builder.ToString();
    }

    private static char VisibleCharacter(Display display, byte value, byte attribute, bool underlineFallback)
    {
        if ((attribute & _nonDisplayMask) == _nonDisplayMask)
            return ' ';

        var character = display.Character(value);
        return underlineFallback && character == ' ' && (attribute & _underlineMask) != 0 ? '_' : character;
    }

    private static void AppendStyle(StringBuilder builder, byte attribute)
    {
        var color = _foregroundCodes[((attribute & 0x10) >> 2) | ((attribute & 8) >> 2) | ((attribute & 2) >> 1)];
        var hidden = (attribute & _nonDisplayMask) == _nonDisplayMask;
        var reverse = !hidden && (attribute & 1) != 0;
        builder.Append(_stylePrefix);
        builder.Append((reverse ? _blackForeground : color).ToString(CultureInfo.InvariantCulture));
        builder.Append(';');
        builder.Append((reverse ? color + _backgroundOffset : _blackBackground).ToString(CultureInfo.InvariantCulture));
        if (!hidden && (attribute & _underlineMask) != 0)
        {
            builder.Append(_underlineStyle);
        }

        builder.Append('m');
    }

    private static void SetColors(byte attribute)
    {
        var color = _colors[((attribute & 0x10) >> 2) | ((attribute & 8) >> 2) | ((attribute & 2) >> 1)];
        var reverse = (attribute & 1) != 0 && (attribute & 7) != 7;
        Console.ForegroundColor = reverse ? ConsoleColor.Black : color;
        Console.BackgroundColor = reverse ? color : ConsoleColor.Black;
    }

    public void Dispose()
    {
        try
        {
            Console.ForegroundColor = _foreground;
            Console.BackgroundColor = _background;
            Console.CursorVisible = true;
            Console.Clear();
        }
        finally
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Console.Title = _title ?? string.Empty;
                }
                else if (_ansi)
                {
                    Console.Write(_restoreTitle);
                }
            }
            finally
            {
                Console.OutputEncoding = _encoding;
            }
        }
    }
}
