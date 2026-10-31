namespace Terminal5250;

internal static class Program
{
    private const string _ebcdicProbe = " A0";
    private static readonly byte[] _ebcdicSignature = [0x40, 0xC1, 0xF0];

    private const int _inputPollMilliseconds = 20;
    private const int _drainBufferLength = 4096;
    private static readonly TimeSpan _disconnectTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan _connectionTimeout = TimeSpan.FromSeconds(30);

    private static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        void cancelHandler(object? _, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            cancellation.Cancel();
        }

        Console.CancelKeyPress += cancelHandler;
        try
        {
            var options = Options.Parse(args);
            if (options.Help)
            {
                Console.Write(Resources.Help);
                return 0;
            }

            if (options.Version)
            {
                Console.WriteLine(Resources.Version);
                return 0;
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var encoding = Encoding.GetEncoding(options.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            if (!encoding.IsSingleByte || !encoding.GetBytes(_ebcdicProbe).AsSpan().SequenceEqual(_ebcdicSignature))
            {
                var message = string.Format(CultureInfo.InvariantCulture, Resources.InvalidValue, Options._codePageOption, options.CodePage);
                throw new ArgumentException(message);
            }

            if (Console.IsInputRedirected || Console.IsOutputRedirected)
                throw new InvalidOperationException(Resources.Redirected);

            using var client = new TcpClient
            {
                NoDelay = true,
            };

            using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            connectionCancellation.CancelAfter(_connectionTimeout);
            try
            {
                await client.ConnectAsync(options.Host, options.Port, connectionCancellation.Token);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                throw new TimeoutException(Resources.ConnectTimeout);
            }

            await using Stream stream = options.Tls ? new SslStream(client.GetStream(), false) : client.GetStream();
            if (stream is SslStream secure)
            {
                try
                {
                    await AuthenticateAsync(secure, options.Host, connectionCancellation.Token);
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
                {
                    throw new TimeoutException(Resources.ConnectTimeout);
                }
            }

            var transport = new TelnetTransport(stream, options);
            var display = new Display(encoding, options.Wide);
            using var renderer = new ConsoleRenderer(options.Host);
            await RunAsync(client, stream, transport, display, renderer, cancellation);
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

    internal static async Task DisconnectAsync(TcpClient client, Stream stream, Task<byte[]?> pending)
    {
        using var timeout = new CancellationTokenSource(_disconnectTimeout);
        try
        {
            if (stream is SslStream secure)
            {
                await secure.ShutdownAsync().WaitAsync(timeout.Token);
            }

            client.Client.Shutdown(SocketShutdown.Send);
            await pending.WaitAsync(timeout.Token);
            var buffer = new byte[_drainBufferLength];
            while (await stream.ReadAsync(buffer).AsTask().WaitAsync(timeout.Token) != 0)
            {
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
        }
        finally
        {
            try
            {
                client.Client.Shutdown(SocketShutdown.Both);
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
            {
            }
        }
    }

    private static async Task RunAsync(TcpClient client, Stream stream, TelnetTransport transport, Display display, ConsoleRenderer renderer,
        CancellationTokenSource cancellation)
    {
        var pending = transport.ReadAsync(cancellation.Token);
        var dirty = true;
        var disconnect = false;
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
                    {
                        disconnect = true;
                        return;
                    }

                    if (ConsoleRenderer.Fits(display))
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

            // canceling a socket read can reset the connection, so finish the connection before canceling its reader.
            if (disconnect)
            {
                await DisconnectAsync(client, stream, pending);
            }

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
