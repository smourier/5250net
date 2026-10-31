namespace Terminal5250;

internal sealed class ConsoleRenderer : IDisposable
{
    private readonly string _host;
    private readonly ConsoleColor _foreground;
    private readonly ConsoleColor _background;
    private readonly Encoding _encoding;
    private int _width;
    private int _height;
    private static readonly ConsoleColor[] _colors =
    [
        ConsoleColor.Green, ConsoleColor.White, ConsoleColor.Red, ConsoleColor.Red,
        ConsoleColor.Cyan, ConsoleColor.Yellow, ConsoleColor.Magenta, ConsoleColor.Blue,
    ];

    internal ConsoleRenderer(string host)
    {
        _host = host;
        _foreground = Console.ForegroundColor;
        _background = Console.BackgroundColor;
        _encoding = Console.OutputEncoding;
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.Clear();
    }

    internal bool Resized() => Console.WindowWidth != _width || Console.WindowHeight != _height;

    internal bool Fits(Display display) => Console.WindowWidth >= display._columns && Console.WindowHeight > display._rows;

    internal void Draw(Display display)
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
            var message = string.Format(CultureInfo.CurrentCulture, Resources.TooSmall, display._columns, display._rows + 1);
            Console.Write(message.AsSpan(0, Math.Min(message.Length, Math.Max(0, _width - 1))));
            return;
        }

        byte attribute = 0x20;
        for (var row = 0; row < display._rows; row++)
        {
            Console.SetCursorPosition(0, row);
            for (var column = 0; column < display._columns; column++)
            {
                var value = display._data[row * display._columns + column];
                if ((value & 0xE0) == 0x20)
                {
                    attribute = value;
                    SetColors(attribute);
                    Console.Write(' ');
                }
                else
                {
                    SetColors(attribute);
                    Console.Write((attribute & 7) == 7 ? ' ' : display.Character(value));
                }
            }
        }

        Console.ResetColor();
        Console.SetCursorPosition(0, display._rows);
        var state = display._inputError ? Resources.InputError : display._locked ? Resources.Locked : Resources.Ready;
        if (display._messageWaiting)
        {
            state += Resources.MessageWaiting;
        }

        if (display._insertMode)
        {
            state += Resources.InsertMode;
        }

        var status = string.Format(CultureInfo.CurrentCulture, Resources.Status,
            _host, display._rows, display._columns, display._cursor / display._columns + 1, display._cursor % display._columns + 1, state);
        var length = Math.Max(0, _width - 1);
        Console.Write(status.Length > length ? status[..length] : status.PadRight(length));
        Console.SetCursorPosition(display._cursor % display._columns, display._cursor / display._columns);
        Console.CursorVisible = !display._locked;
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
        Console.ForegroundColor = _foreground;
        Console.BackgroundColor = _background;
        Console.CursorVisible = true;
        Console.Clear();
        Console.OutputEncoding = _encoding;
    }
}
