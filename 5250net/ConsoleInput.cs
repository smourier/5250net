namespace Terminal5250;

internal sealed class ConsoleInput : IAsyncDisposable
{
    private const int _quietMilliseconds = 200;
    private const int _maximumDrainMilliseconds = 500;
    private const int _pollMilliseconds = 10;
    private readonly bool _controlCAsInput;

    internal ConsoleInput()
    {
        _controlCAsInput = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true;
    }

    internal static bool IsQuit(ConsoleKeyInfo key)
    {
        return key.Modifiers == ConsoleModifiers.Control && key.Key is ConsoleKey.Q or ConsoleKey.C;
    }

    public async ValueTask DisposeAsync()
    {
        var started = Environment.TickCount64;
        var lastInput = started;
        try
        {
            while (Environment.TickCount64 - started < _maximumDrainMilliseconds)
            {
                if (Console.KeyAvailable)
                {
                    Console.ReadKey(true);
                    lastInput = Environment.TickCount64;
                }
                else if (Environment.TickCount64 - lastInput >= _quietMilliseconds)
                {
                    break;
                }

                await Task.Delay(_pollMilliseconds);
            }
        }
        finally
        {
            Console.TreatControlCAsInput = _controlCAsInput;
        }
    }
}
