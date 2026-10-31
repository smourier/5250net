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
        DefaultInputCursor();
        NavigationTransitions();
        ModifierInput();
        FieldRendering();
        HtmlCapture();
        Startup();
        Fuzz();
        await TransportAsync(false);
        await TransportAsync(true);
        await FragmentationAsync();
        await DisconnectWithUnreadDataAsync();
        await CertificateRejectionAsync();
        await CertificateTrustAsync();
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
        Check(!display.Locked && display.Cursor == 89, nameof(ScreenAndInput));
        Check(display.InputFields.Count == 2 && display.InputFields[0].Start == 89, nameof(ScreenAndInput));
        display.Key(Key(ConsoleKey.A, 'a'));
        display.Key(Key(ConsoleKey.Tab));
        display.Key(Key(ConsoleKey.P, 'p'));
        var record = display.Key(Key(ConsoleKey.Enter))!;
        Check(record.AsSpan(10).SequenceEqual(new byte[] { 3, 11, 0xF1, 0x11, 2, 10, 0x81, 0x11, 3, 10, 0x97 }), nameof(ScreenAndInput));
        Check(display.Locked && display.Key(Key(ConsoleKey.Enter)) == null, nameof(ScreenAndInput));
        display.Apply(GdsRecord.Create([4, 0x11, 0, 8, 4, 0x52, 0, 0], 1));
        var repeated = display.Key(Key(ConsoleKey.Enter))!;
        Check(repeated.Length == record.Length, nameof(ScreenAndInput));
    }

    private static void DefaultInputCursor()
    {
        var display = new Display(_encoding, false);
        display.Apply(GdsRecord.Create([4, 0x40, 4, 0x11, 0, 8,
            0x11, 2, 9, 0x1D, 0x40, 0, 0x24, 0, 5, 4, 0x52, 0, 0], 3));
        Check(display.Cursor == 89 && !display.Locked, nameof(DefaultInputCursor));
        display.Key(Key(ConsoleKey.A, 'a'));
        Check(!display.InputError && display.Data[89] == 0x81, nameof(DefaultInputCursor));
        var reply = display.Key(Key(ConsoleKey.Enter))!;
        Check(reply.AsSpan(13).SequenceEqual(new byte[] { 0x11, 2, 10, 0x81 }), nameof(DefaultInputCursor));
    }

    private static void FieldRendering()
    {
        var display = NewDisplay();
        var row = ConsoleRenderer.BuildRow(display, 1, true);
        Check(row.Contains(";4m", StringComparison.Ordinal), nameof(FieldRendering));
        row = ConsoleRenderer.BuildRow(display, 1, false);
        Check(row.AsSpan(9, 5).SequenceEqual("_____"), nameof(FieldRendering));
        display.Key(Key(ConsoleKey.A, 'a'));
        row = ConsoleRenderer.BuildRow(display, 1, true);
        Check(row.Contains('a'), nameof(FieldRendering));
        row = ConsoleRenderer.BuildRow(display, 1, false);
        Check(row.AsSpan(9, 5).SequenceEqual("a____"), nameof(FieldRendering));
        display.Key(Key(ConsoleKey.Tab));
        display.Key(Key(ConsoleKey.P, 'p'));
        Check(!ConsoleRenderer.BuildRow(display, 2, true).Contains('p'), nameof(FieldRendering));
        Check(ConsoleRenderer.BuildRow(display, 2, false).AsSpan(9, 8).SequenceEqual("        "), nameof(FieldRendering));
        Check(display.Data[169] == 0x97, nameof(FieldRendering));
    }

    private static void NavigationTransitions()
    {
        var display = NewDisplay();
        foreach (var key in new[] { ConsoleKey.F3, ConsoleKey.F12 })
        {
            for (var i = 0; i < 10; i++)
            {
                Check(display.Key(Key(key)) != null, nameof(NavigationTransitions));
                var row = (byte)(4 + i % 2);
                display.Apply(GdsRecord.Create([4, 0x11, 0, 8, 1, 1, 0,
                    0x11, row, 9, 0x1D, 0x40, 0, 0x24, 0, 5, 4, 0x52, 0, 8], 3));
                Check(display.Cursor == (row - 1) * 80 + 9, nameof(NavigationTransitions));
                display.Key(Key(ConsoleKey.A, 'a'));
                Check(!display.InputError, nameof(NavigationTransitions));
            }
        }

        display = NewDisplay();
        display.Apply(GdsRecord.Create([4, 0x11, 0, 8, 0x14, 3, 10, 4, 0x52, 0, 8], 3));
        Check(display.Cursor == 169, nameof(NavigationTransitions));
        display.Key(Key(ConsoleKey.F3));
        Check(display.Cursor == 89, nameof(NavigationTransitions));
        display.Apply(GdsRecord.Create([4, 0x40, 4, 0x11, 0, 8, 0x13, 2, 10,
            1, 1, 0, 0x11, 4, 9, 0x1D, 0x40, 0, 0x24, 0, 5, 0x14, 5, 10], 3));
        Check(display.Cursor == 329, nameof(NavigationTransitions));
        display.Apply(GdsRecord.Create([4, 0x11, 0x20, 8], 3));
        Check(display.Cursor == 89, nameof(NavigationTransitions));
    }

    private static void ModifierInput()
    {
        var altGrQ = new ConsoleKeyInfo('@', ConsoleKey.Q, false, true, true);
        Check(!ConsoleInput.IsQuit(altGrQ), nameof(ModifierInput));
        Check(ConsoleInput.IsQuit(Key(ConsoleKey.Q, control: true)), nameof(ModifierInput));
        Check(ConsoleInput.IsQuit(Key(ConsoleKey.C, control: true)), nameof(ModifierInput));
        var display = NewDisplay();
        Check(display.Key(altGrQ) == null && !display.InputError, nameof(ModifierInput));
        Check(display.Data[89] == _encoding.GetBytes("@")[0], nameof(ModifierInput));
        var altGrA = new ConsoleKeyInfo('@', ConsoleKey.A, false, true, true);
        Check(display.Key(altGrA) == null, nameof(ModifierInput));
    }

    private static async Task DisconnectWithUnreadDataAsync()
    {
        using var cancellation = new CancellationTokenSource(_timeout);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var accepting = listener.AcceptTcpClientAsync(cancellation.Token).AsTask();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, cancellation.Token);
        using var server = await accepting;
        var stream = client.GetStream();
        var transport = new TelnetTransport(stream, Options.Parse(["localhost"]));
        var reading = transport.ReadAsync(CancellationToken.None);
        var disconnecting = Terminal5250.Program.DisconnectAsync(client, stream, reading);
        var serverStream = server.GetStream();
        Check(await serverStream.ReadAsync(new byte[1], cancellation.Token) == 0, nameof(DisconnectWithUnreadDataAsync));
        var chunk = new byte[4096];
        Array.Fill(chunk, (byte)0x40);
        for (var i = 0; i < 64; i++)
        {
            await serverStream.WriteAsync(chunk, cancellation.Token);
            await Task.Delay(1, cancellation.Token);
        }

        server.Client.Shutdown(SocketShutdown.Send);
        await disconnecting.WaitAsync(cancellation.Token);
        try
        {
            await reading;
            Check(false, nameof(DisconnectWithUnreadDataAsync));
        }
        catch (InvalidDataException)
        {
            Check(true, nameof(DisconnectWithUnreadDataAsync));
        }
    }

    private static void HtmlCapture()
    {
        var temporary = Path.GetFullPath(Path.GetTempPath());
        var directory = Path.Combine(temporary, nameof(HtmlCapture) + Guid.NewGuid().ToString("N"));
        try
        {
            var options = Options.Parse(["<host&>", "--port", "2323", "--tls", "--tls-trust-all", "--device", "DEV01"]);
            string[] arguments = ["<host&>", "--tls", "--tls-trust-all", "--capture-directory", "captures with spaces", "arg<&é>"];

            var capture = new HtmlScreenCapture(directory, options, arguments, "127.0.0.1:2323");
            var display = new Display(_encoding, true);
            Check(capture.Save(display) == null, nameof(HtmlCapture));
            var text = _encoding.GetBytes("é <b>&");
            var secret = _encoding.GetBytes("SECRET");
            var record = GdsRecord.Create([4, 0x40, 4, 0x11, 0, 8,
                0x11, 1, 1, 0x22, .. text, 0x20,
                0x11, 2, 9, 0x1D, 0x40, 0, 0x24, 0, 5,
                0x11, 3, 9, 0x1D, 0x40, 0, 0x27, 0, 6, .. secret,
                0x13, 2, 10, 4, 0x52, 0, 0], 3);
            display.Apply(record);
            var first = capture.Save(display)!;
            var name = Path.GetFileName(first);
            var expectedPrefix = Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + "_000001_";
            Check(name.StartsWith(expectedPrefix, StringComparison.Ordinal), nameof(HtmlCapture));
            Check(name.EndsWith("Z.html", StringComparison.Ordinal), nameof(HtmlCapture));
            var html = File.ReadAllText(first, Encoding.UTF8);
            Check(html.Contains("é &lt;b&gt;&amp;", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(html.Contains("&lt;host&amp;&gt;", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(!html.Contains("SECRET", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(!html.Contains("<b>", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(html.Contains(".a22{color:#ffffff;background-color:#000000}", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(html.Contains(".a21{color:#000000;background-color:#00ff00}", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(html.Contains("text-decoration:underline", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(html.Contains("data-rows=\"24\" data-columns=\"80\"", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(!html.Replace("\r\n", string.Empty).Contains('\n'), nameof(HtmlCapture));
            Check(capture.Save(display) == null, nameof(HtmlCapture));
            Check(!html.Contains("rel=\"prev\"", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(!html.Contains("rel=\"next\"", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(html.Contains("<dt>TLS</dt><dd>On</dd>", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(html.Contains("127.0.0.1:2323", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(html.Contains("Disabled (trust all)", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(html.Contains("DEV01", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(html.Contains("captures with spaces", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(html.Contains("arg&lt;&amp;é&gt;", StringComparison.Ordinal), nameof(HtmlCapture));
            display.Key(Key(ConsoleKey.A, 'a'));
            Check(capture.Save(display) == null, nameof(HtmlCapture));
            display.Apply(GdsRecord.Create([], 11));
            Check(capture.Save(display) == null, nameof(HtmlCapture));
            display.Apply(record);
            Check(capture.Save(display) == null, nameof(HtmlCapture));
            display.Apply(GdsRecord.Create([4, 0x11, 0, 8, 0x11, 3, 10, 0xC1]));
            Check(capture.Save(display) == null, nameof(HtmlCapture));
            display.Apply(GdsRecord.Create([4, 0x11, 0, 8, 0x11, 1, 2, 0xC1]));
            var second = capture.Save(display)!;
            Check(Path.GetFileName(second).Contains("_000002_", StringComparison.Ordinal), nameof(HtmlCapture));
            var firstUpdated = File.ReadAllText(first);
            var secondHtml = File.ReadAllText(second);
            Check(firstUpdated.Contains("rel=\"next\" href=\"" + Path.GetFileName(second) + "\"", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(!firstUpdated.Contains("rel=\"prev\"", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(secondHtml.Contains("rel=\"prev\" href=\"" + Path.GetFileName(first) + "\"", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(!secondHtml.Contains("rel=\"next\"", StringComparison.Ordinal), nameof(HtmlCapture));
            display.Apply(GdsRecord.Create([4, 0x20, 0]));
            var wide = capture.Save(display)!;
            Check(File.ReadAllText(wide).Contains("data-rows=\"27\" data-columns=\"132\"", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(Directory.GetFiles(directory).Length == 3, nameof(HtmlCapture));
            secondHtml = File.ReadAllText(second);
            var wideHtml = File.ReadAllText(wide);
            Check(secondHtml.Contains("rel=\"next\" href=\"" + Path.GetFileName(wide) + "\"", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(wideHtml.Contains("rel=\"prev\" href=\"" + Path.GetFileName(second) + "\"", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(!wideHtml.Contains("rel=\"next\"", StringComparison.Ordinal), nameof(HtmlCapture));
            Check(File.ReadAllText(first) == firstUpdated, nameof(HtmlCapture));
            Check(capture.Save(display) == null && File.ReadAllText(second) == secondHtml, nameof(HtmlCapture));
        }
        finally
        {
            if (Directory.Exists(directory) && Path.GetDirectoryName(directory) == Path.TrimEndingDirectorySeparator(temporary))
            {
                Directory.Delete(directory, true);
            }
        }
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
        Check(display.Locked && display.Key(Key(ConsoleKey.Enter)) == null, nameof(ReadModes));
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
        Check(display.InputFields.Count == 2 && display.Data[89] == 0x81 && display.Cursor == 90, nameof(SaveRestore));
        Check(display.Key(Key(ConsoleKey.Enter))![16] == 0x81, nameof(SaveRestore));
    }

    private static void WideAndQuery()
    {
        var display = NewDisplay();
        display.Apply(GdsRecord.Create([4, 0x20, 0]));
        Check(display.Columns == 132 && display.Rows == 27, nameof(WideAndQuery));
        display.Apply(GdsRecord.Create([4, 0x40]));
        Check(display.Columns == 80, nameof(WideAndQuery));
        var query = display.Apply(GdsRecord.Create([4, 0xF3, 0, 5, 0xD9, 0x70, 0], 3)).Single();
        Check(query.Length == 71 && query[12] == 0x88 && query[60] == 0x31, nameof(WideAndQuery));
        display.Apply(GdsRecord.Create([4, 0x11, 0, 8, 0x11, 1, 0, 0x1D, 0x40, 0, 0x20, 0, 3, 0xC1]));
        Check(display.InputFields.Single().Start == 0 && display.Data[0] == 0xC1, nameof(WideAndQuery));
    }

    private static void Editing()
    {
        var display = NewDisplay(format: 0x20);
        display.Key(Key(ConsoleKey.A, 'a'));
        Check(display.Data[89] == 0xC1, nameof(Editing));
        display.Key(Key(ConsoleKey.B, 'b'));
        display.Key(Key(ConsoleKey.Backspace));
        Check(display.Data[90] == 0, nameof(Editing));
        display.Key(Key(ConsoleKey.Home));
        display.Key(Key(ConsoleKey.Insert));
        display.Key(Key(ConsoleKey.C, 'c'));
        Check(display.Data[89] == 0xC3 && display.Data[90] == 0xC1, nameof(Editing));
        display.Key(Key(ConsoleKey.Home));
        display.Key(Key(ConsoleKey.End, control: true));
        Check(display.Data[89] == 0 && display.Data[90] == 0, nameof(Editing));
        display.Key(Key(ConsoleKey.UpArrow));
        display.Key(Key(ConsoleKey.A, 'a'));
        Check(display.InputError, nameof(Editing));
        display.Key(Key(ConsoleKey.Escape));
        Check(!display.InputError, nameof(Editing));
    }

    private static void OptionsParsing()
    {
        Check(Options.Parse(["host", "--tls"]).Port == 992, nameof(OptionsParsing));
        Check(Options.Parse(["host", "--port", "1234"]).Port == 1234, nameof(OptionsParsing));
        var equals = Options.Parse(["--port=2223", "host", "--code-page=297", "--device=term01"]);
        Check(equals.Host == "host" && equals.Port == 2223 && equals.CodePage == 297 && equals.Device == "TERM01", nameof(OptionsParsing));
        Check(Options.Parse(["host", "--tls=false"]).Port == 23, nameof(OptionsParsing));
        Check(Options.Parse(["-h"]).Help, nameof(OptionsParsing));
        Check(Options.Parse(["host", "--help"]).Help, nameof(OptionsParsing));
        Check(Options.Parse(["--version"]).Version, nameof(OptionsParsing));
        Check(Options.Parse([]).Help, nameof(OptionsParsing));
        Check(Options.Parse(["host"])._captureDirectory == null, nameof(OptionsParsing));
        Check(Options.Parse(["host", "--capture-directory", "captures"])._captureDirectory == "captures", nameof(OptionsParsing));
        string[][] invalid = [["host", "--port", "0"], ["host", "--port"], ["host", "--device", "bad name"], ["host", "--unknown"],
            ["host", "--port", "not-a-number"], ["host", "extra-host"]];
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
        Check(display.Key(Key(ConsoleKey.Enter)) == null && display.InputError, nameof(FieldRules));
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
        Check(display.Cursor == 89, nameof(FieldRules));
        display.Apply(GdsRecord.Create([4, 0x21, 0x22, 0xF0, 0xF0, 0xF0, 0xF1]));
        Check(display.InputError, nameof(FieldRules));
        display.Key(Key(ConsoleKey.Escape));
        Check(!display.InputError && display.Data[23 * 80] == 0, nameof(FieldRules));
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
            await Terminal5250.Program.AuthenticateAsync(clientStream, Options.Parse(["localhost", "--tls"]), cancellation.Token);
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

    private static async Task CertificateTrustAsync()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pem");
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=5250net Test Root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        using var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(2));
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        var purposes = new OidCollection { new("1.3.6.1.5.5.7.3.1") };

        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(purposes, true));
        using var issued = request.Create(root, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1), new byte[] { 1 });
        using var issuedWithKey = issued.CopyWithPrivateKey(key);
        using var certificate = X509CertificateLoader.LoadPkcs12(issuedWithKey.Export(X509ContentType.Pfx), null);
        try
        {
            File.WriteAllText(file, root.ExportCertificatePem().ReplaceLineEndings("\r\n"), new UTF8Encoding(false));
            var selected = Options.Parse(["localhost", "--tls", "--tls-certificate", file]);
            Check(await AuthenticateCertificateAsync(certificate, selected), nameof(CertificateTrustAsync));
            var wrongName = Options.Parse(["wrong.example", "--tls", "--tls-certificate", file]);
            Check(!await AuthenticateCertificateAsync(certificate, wrongName), nameof(CertificateTrustAsync));
            var trustAll = Options.Parse(["wrong.example", "--tls", "--tls-trust-all"]);
            Check(await AuthenticateCertificateAsync(certificate, trustAll), nameof(CertificateTrustAsync));
            using var selfSignedTemporary = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            using var selfSigned = X509CertificateLoader.LoadPkcs12(selfSignedTemporary.Export(X509ContentType.Pfx), null);
            File.WriteAllText(file, selfSigned.ExportCertificatePem().ReplaceLineEndings("\r\n"), new UTF8Encoding(false));
            Check(await AuthenticateCertificateAsync(selfSigned, selected), nameof(CertificateTrustAsync));
            Check(!await AuthenticateCertificateAsync(certificate, selected), nameof(CertificateTrustAsync));
            File.WriteAllBytes(file, selfSigned.Export(X509ContentType.Cert));
            Check(await AuthenticateCertificateAsync(selfSigned, selected), nameof(CertificateTrustAsync));
            string[][] invalid =
            [
                ["localhost", "--tls-trust-all"],
                ["localhost", "--tls-certificate", file],
                ["localhost", "--tls", "--tls-certificate", file, "--tls-trust-all"],
                ["localhost", "--tls", "--tls-certificate", file + ".missing"],
            ];

            foreach (var arguments in invalid)
            {
                try
                {
                    Options.Parse(arguments);
                    Check(false, nameof(CertificateTrustAsync));
                }
                catch (ArgumentException)
                {
                    Check(true, nameof(CertificateTrustAsync));
                }
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static async Task<bool> AuthenticateCertificateAsync(X509Certificate2 certificate, Options options)
    {
        using var cancellation = new CancellationTokenSource(_timeout);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var accepting = listener.AcceptTcpClientAsync(cancellation.Token).AsTask();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, cancellation.Token);
        using var server = await accepting;
        await using var serverStream = new SslStream(server.GetStream());
        await using var clientStream = new SslStream(client.GetStream());
        var serverHandshake = serverStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = certificate,
        }, cancellation.Token);

        try
        {
            await Terminal5250.Program.AuthenticateAsync(clientStream, options, cancellation.Token);
            await serverHandshake;
            return true;
        }
        catch (AuthenticationException)
        {
            return false;
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
        var reading = transport.ReadAsync(cancellation.Token);
        var disconnecting = Terminal5250.Program.DisconnectAsync(client, clientStream, reading);
        var eof = await serverStream.ReadAsync(new byte[1], cancellation.Token);
        Check(eof == 0, nameof(TransportAsync));
        if (serverStream is SslStream secure)
        {
            await secure.ShutdownAsync();
        }

        server.Client.Shutdown(SocketShutdown.Send);
        await disconnecting.WaitAsync(cancellation.Token);
        Check(await reading == null, nameof(TransportAsync));
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
