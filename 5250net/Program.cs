namespace Terminal5250;

internal static class Program
{
    private const string _ebcdicProbe = " A0";
    private static readonly byte[] _ebcdicSignature = [0x40, 0xC1, 0xF0];

    private const int _inputPollMilliseconds = 20;
    private static readonly TimeSpan _connectionTimeout = TimeSpan.FromSeconds(30);

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args is [Options._helpOption])
        {
            Console.Write(Resources.Help);
            return 0;
        }

        if (args is [Options._versionOption])
        {
            Console.WriteLine(Resources.Version);
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        Console.CancelKeyPress += cancelHandler;
        try
        {
            var options = Options.Parse(args);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var encoding = Encoding.GetEncoding(options._codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            if (!encoding.IsSingleByte || !encoding.GetBytes(_ebcdicProbe).AsSpan().SequenceEqual(_ebcdicSignature))
            {
                var message = string.Format(CultureInfo.InvariantCulture, Resources.InvalidValue, Options._codePageOption, options._codePage);
                throw new ArgumentException(message);
            }

            if (Console.IsInputRedirected || Console.IsOutputRedirected)
                throw new InvalidOperationException(Resources.Redirected);

            using var client = new TcpClient { NoDelay = true };
            using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            connectionCancellation.CancelAfter(_connectionTimeout);
            try
            {
                await client.ConnectAsync(options._host, options._port, connectionCancellation.Token);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                throw new TimeoutException(Resources.ConnectTimeout);
            }

            await using Stream stream = options._tls ? new SslStream(client.GetStream(), false) : client.GetStream();
            if (stream is SslStream secure)
            {
                try
                {
                    await AuthenticateAsync(secure, options._host, connectionCancellation.Token);
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
                {
                    throw new TimeoutException(Resources.ConnectTimeout);
                }
            }

            var transport = new TelnetTransport(stream, options);
            var display = new Display(encoding, options._wide);
            using var renderer = new ConsoleRenderer(options._host);
            await RunAsync(transport, display, renderer, cancellation);
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception) when (exception is IOException or SocketException or AuthenticationException or
            ArgumentException or InvalidOperationException or TimeoutException or NotSupportedException)
        {
            Console.Error.WriteLine(string.Format(CultureInfo.CurrentCulture, Resources.Error, exception.Message));
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    internal static Task AuthenticateAsync(SslStream stream, string host, CancellationToken cancellation)
    {
        return stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = host,
            EnabledSslProtocols = SslProtocols.None,
            CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.Online,
        }, cancellation);
    }

    private static async Task RunAsync(TelnetTransport transport, Display display, ConsoleRenderer renderer,
        CancellationTokenSource cancellation)
    {
        var pending = transport.ReadAsync(cancellation.Token);
        var dirty = true;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                if (pending.IsCompleted)
                {
                    var record = await pending;
                    if (record == null)
                        return;

                    foreach (var reply in display.Apply(record))
                    {
                        await transport.WriteRecordAsync(reply, cancellation.Token);
                    }

                    pending = transport.ReadAsync(cancellation.Token);
                    dirty = true;
                }

                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Q && (key.Modifiers & ConsoleModifiers.Control) != 0)
                        return;

                    if (renderer.Fits(display))
                    {
                        var record = display.Key(key);
                        if (record != null)
                        {
                            await transport.WriteRecordAsync(record, cancellation.Token);
                        }
                    }

                    dirty = true;
                }

                if (dirty || renderer.Resized())
                {
                    renderer.Draw(display);
                    dirty = false;
                }

                await Task.Delay(_inputPollMilliseconds, cancellation.Token);
            }
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                await pending;
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException)
            {
            }
        }
    }
}
