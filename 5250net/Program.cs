namespace Terminal5250;

internal static class Program
{
    private const int _connectionAttemptDelayMilliseconds = 250;
    private const string _serverAuthenticationOid = "1.3.6.1.5.5.7.3.1";
    private const string _ebcdicProbe = " A0";
    private static readonly byte[] _ebcdicSignature = [0x40, 0xC1, 0xF0];

    private const int _inputPollMilliseconds = 20;
    private const int _maximumReplayKeysPerPoll = 32;
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
            if (options.Invocation != null)
                return options.Invocation.Invoke();

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var encoding = Encoding.GetEncoding(options.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            if (!encoding.IsSingleByte || !encoding.GetBytes(_ebcdicProbe).AsSpan().SequenceEqual(_ebcdicSignature))
            {
                var message = string.Format(CultureInfo.InvariantCulture, Resources.InvalidValue, Options._codePageOption, options.CodePage);
                throw new ArgumentException(message);
            }

            if (Console.IsInputRedirected || Console.IsOutputRedirected)
                throw new InvalidOperationException(Resources.Redirected);

            Console.WriteLine(ConsoleRenderer.BuildBanner(DateTime.Now.Year));
            Console.WriteLine();
            using var recorder = options.RecordFile == null ? null : new SessionRecorder(options.RecordFile, options);
            var replay = options.ReplayFile == null ? null : new SessionReplay(options.ReplayFile, options);
            await using var users = new SessionUsers(options, args, cancellation);

            using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            connectionCancellation.CancelAfter(_connectionTimeout);
            TcpClient connected;
            try
            {
                connected = await ConnectAsync(options, connectionCancellation.Token);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                throw new TimeoutException(Resources.ConnectTimeout);
            }

            using var client = connected;
            await using Stream stream = options.Tls ? new SslStream(client.GetStream(), false) : client.GetStream();
            if (stream is SslStream secure)
            {
                try
                {
                    await AuthenticateAsync(secure, options, connectionCancellation.Token);
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
                {
                    throw new TimeoutException(Resources.ConnectTimeout);
                }
            }

            var transport = new TelnetTransport(stream, options);
            var display = new Display(encoding, options.Wide);
            using var renderer = new ConsoleRenderer(options.Host, users.UserNumber);
            await using var input = new ConsoleInput();
            var capture = options.CaptureDirectory == null ? null
                : new HtmlScreenCapture(options.CaptureDirectory, options, args,
                    client.Client.RemoteEndPoint?.ToString(), stream as SslStream);
            var launches = users.LaunchUsersAsync(cancellation.Token);
            try
            {
                await RunAsync(client, stream, transport, display, renderer, capture, recorder, replay, users, launches, cancellation);
                while (!launches.IsCompleted)
                {
                    renderer.Draw(display, Resources.ReplayUsersWaiting);
                    while (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        if (ConsoleInput.IsQuit(key))
                        {
                            cancellation.Cancel();
                        }
                        else
                        {
                            await users.HandleAsync(key, display, null, replay, 0);
                        }
                    }

                    await Task.Delay(_inputPollMilliseconds, cancellation.Token);
                }

                await launches;
            }
            finally
            {
                cancellation.Cancel();
                try
                {
                    await launches;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
            }

            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or SocketException or
            AuthenticationException or CryptographicException or ArgumentException or InvalidOperationException or
            TimeoutException or NotSupportedException or Win32Exception)
        {
            Console.Error.WriteLine(string.Format(CultureInfo.CurrentCulture, Resources.Error, exception.Message));
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    internal static async Task<TcpClient> ConnectAsync(Options options, CancellationToken cancellation)
    {
        var resolved = await Dns.GetHostAddressesAsync(options.Host, cancellation);
        return await ConnectAsync(resolved, options.Port, cancellation);
    }

    internal static async Task<TcpClient> ConnectAsync(IEnumerable<IPAddress> addresses, int port, CancellationToken cancellation)
    {
        var ordered = addresses.Distinct().ToList();
        if (ordered.Count == 0)
            throw new SocketException((int)SocketError.HostNotFound);

        var alternate = ordered.FindIndex(address => address.AddressFamily != ordered[0].AddressFamily);
        if (alternate > 1)
        {
            var address = ordered[alternate];
            ordered.RemoveAt(alternate);
            ordered.Insert(1, address);
        }

        using var attemptsCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var attempts = ordered.Select((address, index) =>
            ConnectAddressAsync(address, port, index * _connectionAttemptDelayMilliseconds, attemptsCancellation.Token)).ToList();
        SocketException? failure = null;
        try
        {
            while (attempts.Count > 0)
            {
                var completed = await Task.WhenAny(attempts);
                attempts.Remove(completed);
                try
                {
                    return await completed;
                }
                catch (SocketException exception)
                {
                    failure = exception;
                }
            }

            throw failure!;
        }
        finally
        {
            attemptsCancellation.Cancel();
            foreach (var attempt in attempts)
            {
                try
                {
                    using var unused = await attempt;
                }
                catch (Exception exception) when (exception is SocketException or OperationCanceledException)
                {
                }
            }
        }
    }

    private static async Task<TcpClient> ConnectAddressAsync(IPAddress address, int port, int delay, CancellationToken cancellation)
    {
        if (delay > 0)
        {
            await Task.Delay(delay, cancellation);
        }

        var client = new TcpClient(address.AddressFamily)
        {
            NoDelay = true,
        };

        try
        {
            await client.ConnectAsync(address, port, cancellation);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    internal static async Task AuthenticateAsync(SslStream stream, Options options, CancellationToken cancellation)
    {
        using var certificate = options.TlsCertificate == null
            ? null
            : X509CertificateLoader.LoadCertificateFromFile(options.TlsCertificate.FullName);
        var authentication = new SslClientAuthenticationOptions
        {
            TargetHost = options.Host,
            EnabledSslProtocols = SslProtocols.None,
            CertificateRevocationCheckMode = X509RevocationMode.Online,
        };

        if (options.TlsTrustAll)
        {
            authentication.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
        }
        else if (certificate != null)
        {
            var policy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                RevocationMode = X509RevocationMode.NoCheck,
            };

            policy.CustomTrustStore.Add(certificate);
            policy.ApplicationPolicy.Add(new Oid(_serverAuthenticationOid));
            authentication.CertificateChainPolicy = policy;
        }

        await stream.AuthenticateAsClientAsync(authentication, cancellation);
    }

    internal static async Task DisconnectAsync(TcpClient client, Stream stream, Task<byte[]?> pending)
    {
        using var timeout = new CancellationTokenSource(_disconnectTimeout);
        Task<int>? draining = null;
        try
        {
            if (stream is SslStream secure)
            {
                await secure.ShutdownAsync().WaitAsync(timeout.Token);
            }

            client.Client.Shutdown(SocketShutdown.Send);
            try
            {
                await pending.WaitAsync(timeout.Token);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or SocketException)
            {
            }

            var buffer = new byte[_drainBufferLength];
            do
            {
                draining = stream.ReadAsync(buffer).AsTask();
            }
            while (await draining.WaitAsync(timeout.Token) != 0);
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

            if (draining != null)
            {
                try
                {
                    await draining;
                }
                catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
                {
                }
            }
        }
    }

    private static async Task RunAsync(TcpClient client, Stream stream, TelnetTransport transport, Display display, ConsoleRenderer renderer,
        HtmlScreenCapture? capture, SessionRecorder? recorder, SessionReplay? replay, SessionUsers? users,
        Task launches, CancellationTokenSource cancellation)
    {
        var pending = transport.ReadAsync(CancellationToken.None);
        var clock = Stopwatch.StartNew();
        var dirty = true;
        var completed = false;
        string? previousActivity = null;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                if (launches.IsFaulted)
                {
                    await launches;
                }

                if (pending.IsCompleted)
                {
                    var record = await pending;
                    if (record == null)
                    {
                        if (replay != null && !replay.IsComplete && !replay.FinishOnDisconnect())
                            throw new InvalidDataException(Resources.ReplayDisconnected);

                        completed = true;
                        return;
                    }

                    foreach (var reply in display.Apply(record))
                    {
                        await transport.WriteRecordAsync(reply, cancellation.Token);
                    }

                    capture?.Save(display);
                    pending = transport.ReadAsync(CancellationToken.None);
                    dirty = true;
                }

                while (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (ConsoleInput.IsQuit(key))
                    {
                        recorder?.Record(key, null, display.InputError, clock.ElapsedMilliseconds);
                        capture?.RecordAction(key, display);
                        cancellation.Cancel();
                        completed = true;
                        return;
                    }

                    replay?.SetPaused(true, clock.ElapsedMilliseconds);
                    var handled = users != null && await users.HandleAsync(key, display, recorder, replay, clock.ElapsedMilliseconds);
                    replay?.SetPaused(users?.IsSelecting == true, clock.ElapsedMilliseconds);
                    if (!handled && replay == null && ConsoleRenderer.Fits(display))
                    {
                        var action = capture == null ? null : HtmlScreenCapture.DescribeAction(key, display);
                        var record = display.Key(key);
                        recorder?.Record(key, record, display.InputError, clock.ElapsedMilliseconds);
                        if (record != null)
                        {
                            capture?.RecordAction(action!);
                            capture?.Save(display, true);
                            await transport.WriteRecordAsync(record, cancellation.Token);
                        }
                    }

                    dirty = true;
                    if (replay == null || handled)
                        break;
                }

                replay?.SetPaused(users?.IsSelecting == true || !ConsoleRenderer.Fits(display), clock.ElapsedMilliseconds);
                for (var replayed = 0; replay != null && replayed < _maximumReplayKeysPerPoll; replayed++)
                {
                    var next = replay.Next(display, clock.ElapsedMilliseconds);
                    if (next == null)
                        break;

                    if (next.Key == null || ConsoleInput.IsQuit(next.Key.Value))
                    {
                        replay.Advance(next, null, display, clock.ElapsedMilliseconds);
                        completed = true;
                        return;
                    }

                    var key = next.Key.Value;
                    var action = capture == null ? null : HtmlScreenCapture.DescribeAction(key, display);
                    var record = display.Key(key);
                    replay.Advance(next, record, display, clock.ElapsedMilliseconds);
                    if (record != null)
                    {
                        capture?.RecordAction(action!);
                        capture?.Save(display, true);
                        await transport.WriteRecordAsync(record, cancellation.Token);
                    }

                    dirty = true;
                }

                var activity = users?.Status ?? replay?.Status;
                if (activity == null && recorder != null)
                {
                    activity = string.Format(CultureInfo.CurrentCulture, Resources.RecordingStatus, Path.GetFileName(recorder.FilePath));
                }

                if (dirty || renderer.Resized() || activity != previousActivity)
                {
                    renderer.Draw(display, activity);
                    previousActivity = activity;
                    dirty = false;
                }

                await Task.Delay(_inputPollMilliseconds, cancellation.Token);
            }

            completed = true;
        }
        finally
        {
            try
            {
                if (completed || cancellation.IsCancellationRequested)
                {
                    recorder?.Complete(display, clock.ElapsedMilliseconds);
                }

                capture?.Save(display, true);
            }
            finally
            {
                if (!completed)
                {
                    cancellation.Cancel();
                }

                // the peer may still be sending a screen, so drain it before releasing the connection.
                await DisconnectAsync(client, stream, pending);
                try
                {
                    await pending;
                }
                catch (Exception exception) when (exception is OperationCanceledException or IOException or InvalidDataException or SocketException)
                {
                }
            }
        }
    }
}
