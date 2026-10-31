namespace Terminal5250.Tests;

internal static class Program
{
    private static int _checks;
    private static Encoding _encoding = null!;
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(15);

    private static async Task Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _encoding = Encoding.GetEncoding(37, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        ScreenAndInput();
        FunctionMasks();
        ReadModes();
        Malformed();
        SaveRestore();
        WideAndQuery();
        Editing();
        OptionsParsing();
        FieldRules();
        Startup();
        Fuzz();
        await TransportAsync(false);
        await TransportAsync(true);
        await FragmentationAsync();
        await CertificateRejectionAsync();
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, Resources.Passed, _checks));
    }

    private static byte[] Screen(byte read = 0x52, byte format = 0)
    {
        return GdsRecord.Create([4, 0x40, 4, 0x11, 0, 8,
            0x11, 2, 9, 0x1D, 0x40, format, 0x24, 0, 5,
            0x11, 3, 9, 0x1D, 0x40, 0, 0x27, 0, 8,
            0x13, 2, 10, 4, read, 0, 0], 3);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, char character = '\0', bool shift = false, bool control = false)
        => new(character, key, shift, false, control);

    private static Display NewDisplay(byte read = 0x52, byte format = 0)
    {
        var display = new Display(_encoding, true);
        display.Apply(Screen(read, format));
        return display;
    }

    private static void Check(bool condition, string name)
    {
        _checks++;
        if (!condition)
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, Resources.Failed, name));
    }

    private static void ScreenAndInput()
    {
        var display = NewDisplay();
        Check(!display._locked && display._cursor == 89, nameof(ScreenAndInput));
        Check(display._inputFields.Count == 2 && display._inputFields[0]._start == 89, nameof(ScreenAndInput));
        display.Key(Key(ConsoleKey.A, 'a'));
        display.Key(Key(ConsoleKey.Tab));
        display.Key(Key(ConsoleKey.P, 'p'));
        var record = display.Key(Key(ConsoleKey.Enter))!;
        Check(record.AsSpan(10).SequenceEqual(new byte[] { 3, 11, 0xF1, 0x11, 2, 10, 0x81, 0x11, 3, 10, 0x97 }), nameof(ScreenAndInput));
        Check(display._locked && display.Key(Key(ConsoleKey.Enter)) == null, nameof(ScreenAndInput));
        display.Apply(GdsRecord.Create([4, 0x11, 0, 8, 4, 0x52, 0, 0], 1));
        var repeated = display.Key(Key(ConsoleKey.Enter))!;
        Check(repeated.Length == record.Length, nameof(ScreenAndInput));
    }

    private static void FunctionMasks()
    {
        var display = NewDisplay();
        display.Key(Key(ConsoleKey.A, 'a'));
        var record = display.Key(Key(ConsoleKey.F1))!;
        Check(record[12] == 0x31 && record.Length > 13, nameof(FunctionMasks));
        display = NewDisplay();
        display.Apply(GdsRecord.Create([4, 0x40, 4, 0x11, 0, 8, 1, 7, 0, 0, 0, 0, 0, 0, 1, .. Screen().AsSpan(16).ToArray()], 3));
        display.Key(Key(ConsoleKey.A, 'a'));
        record = display.Key(Key(ConsoleKey.F1))!;
        Check(record.Length == 13, nameof(FunctionMasks));
        display = NewDisplay();
        record = display.Key(Key(ConsoleKey.F12, shift: true))!;
        Check(record[12] == 0xBC, nameof(FunctionMasks));
    }

    private static void ReadModes()
    {
        var display = NewDisplay(0x42);
        display.Key(Key(ConsoleKey.A, 'a'));
        var record = display.Key(Key(ConsoleKey.Enter))!;
        Check(record.Length == 26 && record[13] == 0x81 && record[14] == 0x40, nameof(ReadModes));
        display = NewDisplay(0x82);
        display.Key(Key(ConsoleKey.RightArrow));
        display.Key(Key(ConsoleKey.A, 'a'));
        record = display.Key(Key(ConsoleKey.Enter))!;
        Check(record[16] == 0 && record[17] == 0x81, nameof(ReadModes));
        display = NewDisplay();
        display.Key(Key(ConsoleKey.A, 'a'));
        var reply = display.Apply(GdsRecord.Create([4, 0x72], 6)).Single();
        Check(reply[12] == 0 && reply.Length == 26, nameof(ReadModes));
        display.Apply(GdsRecord.Create([], 10));
        Check(display._locked && display.Key(Key(ConsoleKey.Enter)) == null, nameof(ReadModes));
    }

    private static void Malformed()
    {
        byte[][] payloads = [[4], [4, 0x11], [4, 0x11, 0, 8, 0x11, 0, 1],
            [4, 0x11, 0, 8, 0x1D, 0x40], [4, 0x11, 0, 8, 0x10, 0, 5, 0xC1]];
        foreach (var payload in payloads)
        {
            try
            {
                NewDisplay().Apply(GdsRecord.Create(payload));
                Check(false, nameof(Malformed));
            }
            catch (InvalidDataException)
            {
                Check(true, nameof(Malformed));
            }
        }

        var record = Screen();
        record[0] = 0xFF;
        try
        {
            GdsRecord.Validate(record);
            Check(false, nameof(Malformed));
        }
        catch (InvalidDataException)
        {
            Check(true, nameof(Malformed));
        }
    }

    private static void SaveRestore()
    {
        var display = NewDisplay();
        display.Key(Key(ConsoleKey.A, 'a'));
        var snapshot = display.Apply(GdsRecord.Create([4, 2], 4)).Single();
        display.Apply(GdsRecord.Create([4, 0x40]));
        display.Apply(GdsRecord.Create(snapshot.AsSpan(10), 5));
        Check(display._inputFields.Count == 2 && display._data[89] == 0x81 && display._cursor == 90, nameof(SaveRestore));
        Check(display.Key(Key(ConsoleKey.Enter))![16] == 0x81, nameof(SaveRestore));
    }

    private static void WideAndQuery()
    {
        var display = NewDisplay();
        display.Apply(GdsRecord.Create([4, 0x20, 0]));
        Check(display._columns == 132 && display._rows == 27, nameof(WideAndQuery));
        display.Apply(GdsRecord.Create([4, 0x40]));
        Check(display._columns == 80, nameof(WideAndQuery));
        var query = display.Apply(GdsRecord.Create([4, 0xF3, 0, 5, 0xD9, 0x70, 0], 3)).Single();
        Check(query.Length == 71 && query[12] == 0x88 && query[60] == 0x31, nameof(WideAndQuery));
        display.Apply(GdsRecord.Create([4, 0x11, 0, 8, 0x11, 1, 0, 0x1D, 0x40, 0, 0x20, 0, 3, 0xC1]));
        Check(display._inputFields.Single()._start == 0 && display._data[0] == 0xC1, nameof(WideAndQuery));
    }

    private static void Editing()
    {
        var display = NewDisplay(format: 0x20);
        display.Key(Key(ConsoleKey.A, 'a'));
        Check(display._data[89] == 0xC1, nameof(Editing));
        display.Key(Key(ConsoleKey.B, 'b'));
        display.Key(Key(ConsoleKey.Backspace));
        Check(display._data[90] == 0, nameof(Editing));
        display.Key(Key(ConsoleKey.Home));
        display.Key(Key(ConsoleKey.Insert));
        display.Key(Key(ConsoleKey.C, 'c'));
        Check(display._data[89] == 0xC3 && display._data[90] == 0xC1, nameof(Editing));
        display.Key(Key(ConsoleKey.Home));
        display.Key(Key(ConsoleKey.End, control: true));
        Check(display._data[89] == 0 && display._data[90] == 0, nameof(Editing));
        display.Key(Key(ConsoleKey.UpArrow));
        display.Key(Key(ConsoleKey.A, 'a'));
        Check(display._inputError, nameof(Editing));
        display.Key(Key(ConsoleKey.Escape));
        Check(!display._inputError, nameof(Editing));
    }

    private static void OptionsParsing()
    {
        Check(Options.Parse(["host", "--tls"])._port == 992, nameof(OptionsParsing));
        Check(Options.Parse(["host", "--port", "1234"])._port == 1234, nameof(OptionsParsing));
        string[][] invalid = [["host", "--port", "0"], ["host", "--port"], ["host", "--device", "bad name"], ["host", "--unknown"]];
        foreach (var args in invalid)
        {
            try
            {
                Options.Parse(args);
                Check(false, nameof(OptionsParsing));
            }
            catch (ArgumentException)
            {
                Check(true, nameof(OptionsParsing));
            }
        }
    }

    private static void FieldRules()
    {
        var display = NewDisplay(format: 0x40);
        display.Key(Key(ConsoleKey.A, 'a'));
        Check(display.Key(Key(ConsoleKey.Enter)) == null && display._inputError, nameof(FieldRules));
        display.Key(Key(ConsoleKey.Escape));
        display.Key(Key(ConsoleKey.Enter, control: true));
        Check(display.Key(Key(ConsoleKey.Enter)) != null, nameof(FieldRules));
        display = NewDisplay(format: 0x80);
        for (var i = 0; i < 4; i++)
        {
            Check(display.Key(Key(ConsoleKey.A, 'a')) == null, nameof(FieldRules));
        }

        Check(display.Key(Key(ConsoleKey.A, 'a')) != null, nameof(FieldRules));
        display = NewDisplay();
        display.Key(Key(ConsoleKey.A, 'a'));
        display.Key(Key(ConsoleKey.Tab));
        display.Key(Key(ConsoleKey.P, 'p'));
        display.Key(Key(ConsoleKey.Tab, shift: true));
        Check(display._cursor == 89, nameof(FieldRules));
        display.Apply(GdsRecord.Create([4, 0x21, 0x22, 0xF0, 0xF0, 0xF0, 0xF1]));
        Check(display._inputError, nameof(FieldRules));
        display.Key(Key(ConsoleKey.Escape));
        Check(!display._inputError && display._data[23 * 80] == 0, nameof(FieldRules));
    }

    private static void Startup()
    {
        var response = new byte[73];
        response[1] = 73;
        response[2] = 0x12;
        response[3] = 0xA0;
        response[4] = 0x90;
        _encoding.GetBytes("I902").CopyTo(response, 16);
        Check(NewDisplay().Apply(response).Count == 0, nameof(Startup));
        _encoding.GetBytes("8902").CopyTo(response, 16);
        try
        {
            NewDisplay().Apply(response);
            Check(false, nameof(Startup));
        }
        catch (InvalidDataException)
        {
            Check(true, nameof(Startup));
        }
    }

    private static void Fuzz()
    {
        var random = new Random(5250);
        for (var i = 0; i < 1000; i++)
        {
            var data = new byte[random.Next(0, 80)];
            random.NextBytes(data);
            try
            {
                NewDisplay().Apply(GdsRecord.Create([4, 0x11, 0, 8, .. data]));
            }
            catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
            {
            }
        }

        Check(true, nameof(Fuzz));
    }

    private static async Task CertificateRejectionAsync()
    {
        using var cancellation = new CancellationTokenSource(_timeout);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var accepting = listener.AcceptTcpClientAsync(cancellation.Token).AsTask();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, cancellation.Token);
        using var server = await accepting;
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var temporary = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var certificate = X509CertificateLoader.LoadPkcs12(temporary.Export(X509ContentType.Pfx), null);
        await using var serverStream = new SslStream(server.GetStream());
        await using var clientStream = new SslStream(client.GetStream());
        var serverHandshake = serverStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = certificate,
        }, cancellation.Token);

        try
        {
            await Terminal5250.Program.AuthenticateAsync(clientStream, "localhost", cancellation.Token);
            Check(false, nameof(CertificateRejectionAsync));
        }
        catch (AuthenticationException)
        {
            Check(true, nameof(CertificateRejectionAsync));
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                await serverHandshake;
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or AuthenticationException)
            {
            }
        }
    }

    private static async Task TransportAsync(bool tls)
    {
        using var cancellation = new CancellationTokenSource(_timeout);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var accept = listener.AcceptTcpClientAsync(cancellation.Token).AsTask();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, cancellation.Token);
        using var server = await accept;
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var temporaryCertificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var certificate = X509CertificateLoader.LoadPkcs12(temporaryCertificate.Export(X509ContentType.Pfx), null);
        await using Stream serverStream = tls ? new SslStream(server.GetStream()) : server.GetStream();
        await using Stream clientStream = tls ? new SslStream(client.GetStream(), false, (_, _, _, _) => true) : client.GetStream();
        if (tls)
        {
            var serverHandshake = ((SslStream)serverStream).AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
            }, cancellation.Token);

            var clientHandshake = ((SslStream)clientStream).AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
            }, cancellation.Token);

            await await Task.WhenAny(serverHandshake, clientHandshake);
            await Task.WhenAll(serverHandshake, clientHandshake);
        }

        var options = Options.Parse(["localhost", "--device", "TEST01"]);
        var transport = new TelnetTransport(clientStream, options);
        var pending = transport.ReadAsync(cancellation.Token);
        byte[] negotiation = [255, 253, 24, 255, 253, 39, 255, 253, 0, 255, 251, 0, 255, 253, 25, 255, 251, 25];

        await serverStream.WriteAsync(negotiation, cancellation.Token);
        var responses = new byte[18];
        await serverStream.ReadExactlyAsync(responses, cancellation.Token);
        byte[] expected = [255, 251, 24, 255, 251, 39, 255, 251, 0, 255, 253, 0, 255, 251, 25, 255, 253, 25];

        Check(responses.AsSpan().SequenceEqual(expected), nameof(TransportAsync));
        await serverStream.WriteAsync(new byte[] { 255, 250, 24, 1, 255, 240 }, cancellation.Token);
        var terminal = await ReadSubnegotiationAsync(serverStream, cancellation.Token);
        Check(terminal.AsSpan(4, terminal.Length - 6).SequenceEqual(Encoding.ASCII.GetBytes("IBM-3179-2")), nameof(TransportAsync));
        await serverStream.WriteAsync(new byte[] { 255, 250, 39, 1, 3, 255, 240 }, cancellation.Token);
        var environment = await ReadSubnegotiationAsync(serverStream, cancellation.Token);
        Check(Encoding.ASCII.GetString(environment).Contains("TEST01", StringComparison.Ordinal), nameof(TransportAsync));
        List<byte> wire = [];
        foreach (var b in Screen())
        {
            wire.Add(b);
            if (b == 255)
            {
                wire.Add(b);
            }
        }

        wire.AddRange([255, 239]);
        foreach (var b in wire)
        {
            await serverStream.WriteAsync(new byte[] { b }, cancellation.Token);
        }

        var received = await pending;
        var display = new Display(_encoding, false);
        display.Apply(received!);
        display.Key(Key(ConsoleKey.A, 'a'));
        var input = display.Key(Key(ConsoleKey.Enter))!;
        await transport.WriteRecordAsync(input, cancellation.Token);
        var answer = new byte[input.Length + 2];
        await serverStream.ReadExactlyAsync(answer, cancellation.Token);
        Check(answer.AsSpan(0, input.Length).SequenceEqual(input) && answer[^2] == 255 && answer[^1] == 239, nameof(TransportAsync));
        await transport.WriteRecordAsync(GdsRecord.Create([255]), cancellation.Token);
        answer = new byte[14];
        await serverStream.ReadExactlyAsync(answer, cancellation.Token);
        Check(answer.AsSpan(10).SequenceEqual(new byte[] { 255, 255, 255, 239 }), nameof(TransportAsync));
    }

    private static async Task<byte[]> ReadSubnegotiationAsync(Stream stream, CancellationToken cancellation)
    {
        var data = new List<byte>();
        var single = new byte[1];
        while (data.Count < 4096)
        {
            await stream.ReadExactlyAsync(single, cancellation);
            data.Add(single[0]);
            if (data.Count > 1 && data[^2] == 255 && data[^1] == 240)
                return [.. data];
        }

        throw new InvalidDataException();
    }

    private static async Task FragmentationAsync()
    {
        using var stream = new MemoryStream([255, 253]);
        var transport = new TelnetTransport(stream, Options.Parse(["host"]));
        try
        {
            await transport.ReadAsync(CancellationToken.None);
            Check(false, nameof(FragmentationAsync));
        }
        catch (EndOfStreamException)
        {
            Check(true, nameof(FragmentationAsync));
        }
    }
}
