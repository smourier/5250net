namespace Terminal5250;

internal sealed class TelnetTransport(Stream stream, Options options)
{
#pragma warning disable IDE1006
    private const byte IAC = 255;
    private const byte DONT = 254;
    private const byte DO = 253;
    private const byte WONT = 252;
    private const byte WILL = 251;
    private const byte SB = 250;
    private const byte SE = 240;
    private const byte EOR = 239;
    private const byte TELOPT_BINARY = 0;
    private const byte TELOPT_SGA = 3;
    private const byte TELOPT_TTYPE = 24;
    private const byte TELOPT_EOR = 25;
    private const byte TELOPT_NEW_ENVIRON = 39;
    private const byte TELQUAL_IS = 0;
    private const byte TELQUAL_SEND = 1;
    private const byte NEW_ENV_VAR = 0;
    private const byte NEW_ENV_VALUE = 1;
    private const byte NEW_ENV_ESC = 2;
    private const byte NEW_ENV_USERVAR = 3;
#pragma warning restore IDE1006

    private const int _optionCount = 256;
    private const int _bufferSize = 4096;
    private const string _narrowTerminal = "IBM-3179-2";
    private const string _wideTerminal = "IBM-3477-FC";
    private const string _deviceVariable = "DEVNAME";
    private const string _keyboardVariable = "KBDTYPE";
    private const string _codePageVariable = "CODEPAGE";
    private const int _maximumRecord = ushort.MaxValue;
    private const int _maximumSubnegotiation = 4096;
    private readonly byte[] _buffer = new byte[_bufferSize];
    private readonly bool[] _local = new bool[_optionCount];
    private readonly bool[] _remote = new bool[_optionCount];
    private readonly SemaphoreSlim _writeLock = new(1);
    private int _offset;
    private int _count;

    internal async Task<byte[]?> ReadAsync(CancellationToken cancellation)
    {
        var record = new List<byte>();
        while (true)
        {
            var value = await NextAsync(cancellation);
            if (value < 0)
            {
                if (record.Count != 0)
                    throw new EndOfStreamException(Resources.Truncated);

                return null;
            }

            if (value != IAC)
            {
                record.Add((byte)value);
            }
            else
            {
                var verb = await RequiredAsync(cancellation);
                switch (verb)
                {
                    case IAC:
                        record.Add(IAC);
                        break;

                    case EOR:
                        if (!_local[TELOPT_BINARY] || !_remote[TELOPT_BINARY] || !_local[TELOPT_EOR] || !_remote[TELOPT_EOR])
                            throw new InvalidDataException(Resources.NegotiationRefused);

                        return [.. record];

                    case WILL:
                    case WONT:
                    case DO:
                    case DONT:
                        await NegotiateAsync(verb, await RequiredAsync(cancellation), cancellation);
                        break;

                    case SB:
                        await SubnegotiationAsync(cancellation);
                        break;
                }
            }

            if (record.Count > _maximumRecord)
                throw new InvalidDataException(Resources.RecordTooLarge);
        }
    }

    internal async Task WriteRecordAsync(byte[] record, CancellationToken cancellation)
    {
        var wire = new List<byte>(record.Length + 2);
        Escape(wire, record);
        wire.AddRange([IAC, EOR]);
        await SendAsync([.. wire], cancellation);
    }

    private async Task NegotiateAsync(byte verb, byte option, CancellationToken cancellation)
    {
        var local = verb is DO or DONT;
        var enabled = local ? _local : _remote;
        var supported = option is TELOPT_BINARY or TELOPT_SGA or TELOPT_EOR || local && option is TELOPT_TTYPE or TELOPT_NEW_ENVIRON;
        if (verb is WONT or DONT)
        {
            if (enabled[option])
            {
                enabled[option] = false;
                await SendAsync([IAC, local ? (byte)WONT : (byte)DONT, option], cancellation);
            }

            if (option is TELOPT_BINARY or TELOPT_EOR)
                throw new InvalidDataException(Resources.NegotiationRefused);

            return;
        }

        if (!supported)
        {
            await SendAsync([IAC, local ? (byte)WONT : (byte)DONT, option], cancellation);
            return;
        }

        if (!enabled[option])
        {
            enabled[option] = true;
            await SendAsync([IAC, local ? (byte)WILL : (byte)DO, option], cancellation);
        }
    }

    private async Task SubnegotiationAsync(CancellationToken cancellation)
    {
        var option = await RequiredAsync(cancellation);
        var data = new List<byte>();
        while (true)
        {
            var value = await RequiredAsync(cancellation);
            if (value == IAC)
            {
                value = await RequiredAsync(cancellation);
                if (value == SE)
                    break;

                if (value != IAC)
                    throw new InvalidDataException(Resources.BadTelnet);
            }

            data.Add(value);
            if (data.Count > _maximumSubnegotiation)
                throw new InvalidDataException(Resources.BadTelnet);
        }

        if (data.Count == 0 || data[0] != TELQUAL_SEND || !_local[option])
            return;

        List<byte> response = [TELQUAL_IS];
        if (option == TELOPT_TTYPE)
        {
            response.AddRange(Encoding.ASCII.GetBytes(options.Wide ? _wideTerminal : _narrowTerminal));
        }
        else if (option == TELOPT_NEW_ENVIRON)
        {
            var requested = RequestedVariables(data);
            AddVariable(response, requested, _deviceVariable, options.Device);
            AddVariable(response, requested, _keyboardVariable, options.Keyboard);
            AddVariable(response, requested, _codePageVariable, options.CodePage.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            return;
        }

        List<byte> wire = [IAC, SB, option];
        Escape(wire, response);
        wire.AddRange([IAC, SE]);
        await SendAsync([.. wire], cancellation);
    }

    private static HashSet<string> RequestedVariables(List<byte> data)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var name = new List<byte>();
        for (var i = 1; i <= data.Count; i++)
        {
            if (i == data.Count || data[i] is NEW_ENV_VAR or NEW_ENV_USERVAR)
            {
                if (i > 1)
                {
                    names.Add(Encoding.ASCII.GetString([.. name]));
                    name.Clear();
                }
            }
            else
            {
                if (data[i] == NEW_ENV_ESC && ++i == data.Count)
                    throw new InvalidDataException(Resources.BadTelnet);

                name.Add(data[i]);
            }
        }

        return names;
    }

    private static void AddVariable(List<byte> data, HashSet<string> requested, string name, string value)
    {
        if (value.Length == 0 || !requested.Contains(name) && !requested.Contains(string.Empty))
            return;

        data.Add(NEW_ENV_USERVAR);
        data.AddRange(Encoding.ASCII.GetBytes(name));
        data.Add(NEW_ENV_VALUE);
        foreach (var b in Encoding.ASCII.GetBytes(value))
        {
            if (b <= NEW_ENV_USERVAR)
            {
                data.Add(NEW_ENV_ESC);
            }

            data.Add(b);
        }
    }

    private static void Escape(List<byte> destination, IEnumerable<byte> source)
    {
        foreach (var b in source)
        {
            destination.Add(b);
            if (b == IAC)
            {
                destination.Add(b);
            }
        }
    }

    private async Task SendAsync(byte[] bytes, CancellationToken cancellation)
    {
        await _writeLock.WaitAsync(cancellation);
        try
        {
            await stream.WriteAsync(bytes, cancellation);
            await stream.FlushAsync(cancellation);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async ValueTask<byte> RequiredAsync(CancellationToken cancellation)
    {
        var value = await NextAsync(cancellation);
        if (value < 0)
            throw new EndOfStreamException(Resources.Truncated);

        return (byte)value;
    }

    private async ValueTask<int> NextAsync(CancellationToken cancellation)
    {
        if (_offset == _count)
        {
            _count = await stream.ReadAsync(_buffer, cancellation);
            _offset = 0;
            if (_count == 0)
                return -1;
        }

        return _buffer[_offset++];
    }
}
