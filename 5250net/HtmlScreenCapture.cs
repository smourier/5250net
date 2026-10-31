namespace Terminal5250;

internal sealed class HtmlScreenCapture
{
    private const byte _normalAttribute = 0x20;
    private const byte _attributeLimit = 0x40;
    private const byte _attributeMask = 0xE0;
    private const byte _nonDisplayMask = 7;
    private const byte _underlineMask = 4;
    private const string _black = "#000000";
    private const string _lineEnding = "\r\n";
    private const string _fileNameFormat = "{0}_{1:D6}_{2:yyyyMMddTHHmmssfffffffZ}.html";
    private const string _screenOpen = "<pre id=\"screen\" class=\"screen\" data-rows=\"{0}\" data-columns=\"{1}\">";
    private const string _indexName = "index.html";
    private const string _sessionNameFormat = "{0}_{1:yyyyMMddTHHmmssfffffffZ}";
    private const string _indexRelation = "index";
    private const string _indexHeaderOpen = "<header class=\"capture-footer capture-session\"><h1>";
    private const string _indexHeaderClose = "</header>";
    private const string _indexLinksOpen = "<ol class=\"capture-index-links\">";
    private const string _indexLinksClose = "</ol>";
    private const string _indexLinkFormat = "<li><a href=\"{0}\">{1}</a></li>";
    private const string _frameHeadingFormat = "<section class=\"capture-entry\"><h2><a href=\"{0}\">{1}</a></h2>";
    private const string _frameOpenFormat = """
        <div class="capture-frame-scroll"><iframe class="capture-frame" src="{0}#screen" title="{1}" loading="lazy"
        style="--screen-columns:{2};--screen-rows:{3}"></iframe></div></section>
        """;
    private const string _screenClose = "</span></pre>";
    private const string _spanOpen = "<span class=\"a{0:X2}\">";
    private const string _spanClose = "</span>";
    private const string _colorProperty = "{color:";
    private const string _backgroundProperty = ";background-color:";
    private const string _underlineProperty = ";text-decoration:underline";
    private const string _styleClose = "}";
    private const string _styleClassPrefix = ".a";
    private const string _styleEnd = "</style></head><body>";
    private const string _indexStyleEnd = "</style></head><body class=\"capture-gallery\">";
    private const string _nextPlaceholder = "<span data-next></span>";
    private const string _navigationOpen = "<nav class=\"capture-nav\">";
    private const string _navigationClose = "</nav>";
    private const string _footerOpen = "<footer class=\"capture-footer\"><h1>";
    private const string _headingClose = "</h1><dl class=\"capture-details\">";
    private const string _detailsClose = "</dl>";
    private const string _footerClose = "</footer>";
    private const string _actionOpen = "<div class=\"capture-action\" aria-label=\"{0}\">";
    private const string _actionClose = "</div>";
    private const string _keyFormat = "<kbd>{0}</kbd>";
    private const string _actionFactOpen = "<dt>{0}</dt><dd>";
    private const string _actionFactClose = "</dd>";
    private const string _actionSeparator = "<span class=\"capture-key-separator\" aria-hidden=\"true\">&#8594;</span>";
    private const string _modifierSeparator = "<span class=\"capture-key-separator\" aria-hidden=\"true\">+</span>";
    private const string _choicePlaceholder = "{0}";
    private const string _factFormat = "<dt>{0}</dt><dd>{1}</dd>";
    private const string _linkFormat = "<a rel=\"{0}\" href=\"{1}\">{2}</a>";
    private const string _previousRelation = "prev";
    private const string _nextRelation = "next";
    private const string _argumentsOpen = "<div class=\"capture-arguments\"><strong>";
    private const string _argumentsValue = "</strong><code>";
    private const string _argumentsClose = "</code></div>";
    private const string _timestampFormat = "yyyy-MM-dd HH:mm:ss.fffffff 'UTC'";
    private const string _numberFormat = "D6";
    private const string _temporaryExtension = ".tmp";
    private const string _quote = "\"";
    private const string _escapedQuote = "\\\"";
    private const string _argumentSeparator = " ";
    private const string _documentEnd = "</body></html>";
    private const string _documentStart = """
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>
        """;
    private const string _documentStyle = """
        </title><style>
        html,body{margin:0;background:#000000;color:#00ff00}
        body{padding:16px}
        .capture-gallery{background:#1b2430;color:#dfe7ef;min-height:100vh;box-sizing:border-box}
        .capture-gallery .capture-frame{border-color:#4b5563}
        .screen:target + .capture-footer{display:none}
        .capture-session{margin-top:0;margin-bottom:24px}
        .capture-index-links{display:flex;flex-wrap:wrap;gap:8px 20px;padding-left:20px}
        .capture-index-links a,.capture-entry h2 a{color:#b8d8ff;text-decoration:none}
        .capture-index-links a:hover,.capture-entry h2 a:hover{text-decoration:underline}
        .capture-entry{margin:24px 0}.capture-entry h2{font:14px/1.5 system-ui,sans-serif}
        .capture-frame-scroll{overflow-x:auto}
        .capture-frame{display:block;box-sizing:content-box;border:1px solid #333;background:#000;
        font:16px/1.2 Consolas,"Cascadia Mono","DejaVu Sans Mono",monospace;
        width:calc(var(--screen-columns) * 1ch + 32px);height:calc(var(--screen-rows) * 1.2em + 32px)}
        .screen{display:inline-block;margin:0;white-space:pre;font-size:16px;line-height:1.2;letter-spacing:0;
        font-family:Consolas,"Cascadia Mono","DejaVu Sans Mono",monospace;font-variant-ligatures:none}
        .capture-action{display:flex;align-items:center;flex-wrap:wrap;gap:8px;margin:10px 0;color:#b9c8d8}
        .capture-action kbd{display:inline-block;min-width:20px;padding:5px 10px;border:1px solid #718096;
        border-bottom:4px solid #4a5568;border-radius:6px;background:#edf2f7;color:#17212e;
        box-shadow:0 2px 3px #0006;font:600 14px/1.3 Consolas,"DejaVu Sans Mono",monospace;text-align:center}
        .capture-key-separator{color:#9ab5ce;font-size:18px}
        .capture-footer{max-width:1280px;margin-top:20px;padding:16px;border:1px solid #333;border-radius:6px;
        color:#ccc;background:#111;font:13px/1.5 system-ui,sans-serif}
        .capture-footer h1{margin:0 0 12px;font-size:15px;color:#eee}
        .capture-details{display:grid;grid-template-columns:max-content minmax(0,1fr) max-content minmax(0,1fr);gap:5px 14px;margin:0}
        .capture-details dt{color:#888}.capture-details dd{margin:0;overflow-wrap:anywhere}
        .capture-arguments{margin-top:12px;padding-top:12px;border-top:1px solid #333}
        .capture-arguments strong{display:block;color:#888;font-weight:400}
        .capture-arguments code{display:block;white-space:pre-wrap;overflow-wrap:anywhere}
        .capture-nav{display:flex;gap:12px;justify-content:space-between;margin-top:14px}
        .capture-nav a{padding:6px 14px;border:1px solid #444;border-radius:4px;color:#b8d8ff;background:#1d2835;text-decoration:none}
        .capture-nav a:hover{background:#293d52}.capture-nav a[rel=next]{margin-left:auto}
        .capture-nav span:empty{display:none}
        @media(max-width:700px){.capture-details{grid-template-columns:max-content minmax(0,1fr)}}
        """;
    private static readonly string[] _colors =
    [
        "#00ff00", "#ffffff", "#ff0000", "#ff0000",
        "#00ffff", "#ffff00", "#ff00ff", "#0000ff",
    ];

    private static readonly HtmlEncoder _encoder = HtmlEncoder.Create(UnicodeRanges.All);
    private static readonly Encoding _utf8 = new UTF8Encoding(false);
    private readonly string _directory;
    private readonly string _indexPath;
    private readonly DateTimeOffset _sessionStarted = DateTimeOffset.UtcNow;
    private readonly List<CapturedFrame> _frames = [];
    private readonly string _host;
    private readonly string _connectionDetails;
    private readonly string _arguments;
    private string? _action;
    private string? _lastPath;
    private string? _lastDocument;
    private string? _lastScreen;
    private int _screenNumber;
    private long _lastRevision;

    internal HtmlScreenCapture(string directory, Options options, IEnumerable<string> arguments,
        string? remoteEndpoint = null, SslStream? tls = null)
    {
        var session = string.Format(CultureInfo.InvariantCulture, _sessionNameFormat, Environment.ProcessId, _sessionStarted);
        _directory = Path.Combine(Path.GetFullPath(directory), session);
        _indexPath = Path.Combine(_directory, _indexName);
        _host = options.Host;
        _arguments = string.Join(_argumentSeparator, arguments.Select(argument =>
            _quote + argument.Replace(_quote, _escapedQuote, StringComparison.Ordinal) + _quote));
        var details = new StringBuilder();
        AppendFact(details, Resources.HtmlServer, options.Host);
        AppendFact(details, Resources.HtmlPort, options.Port.ToString(CultureInfo.InvariantCulture));
        AppendFact(details, Resources.HtmlRemoteEndpoint, remoteEndpoint ?? Resources.HtmlUnknown);
        AppendFact(details, Resources.HtmlTls, options.Tls ? Resources.HtmlOn : Resources.HtmlOff);
        var validation = !options.Tls ? Resources.HtmlNotApplicable
            : options._tlsTrustAll ? Resources.HtmlTrustAll
            : options._tlsCertificate?.FullName ?? Resources.HtmlSystemTrust;
        AppendFact(details, Resources.HtmlValidation, validation);
        if (options.Tls)
        {
            AppendFact(details, Resources.HtmlTlsProtocol, tls?.SslProtocol.ToString() ?? Resources.HtmlUnknown);
            AppendFact(details, Resources.HtmlTlsCipher, tls?.NegotiatedCipherSuite.ToString() ?? Resources.HtmlUnknown);
        }

        AppendFact(details, Resources.HtmlCodePage, options.CodePage.ToString(CultureInfo.InvariantCulture));
        AppendFact(details, Resources.HtmlKeyboard, options.Keyboard);
        AppendFact(details, Resources.HtmlDevice, options.Device.Length == 0 ? Resources.HtmlServerSelected : options.Device);
        _connectionDetails = details.ToString();
    }

    internal void RecordAction(ConsoleKeyInfo key, Display? display = null)
    {
        _action = DescribeAction(key, display);
    }

    internal void RecordAction(string action)
    {
        _action = action;
    }

    internal static string DescribeAction(ConsoleKeyInfo key, Display? display = null)
    {
        var action = key.Key.ToString();
        if (key.Key == ConsoleKey.Enter && (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0
            && display != null && NumericChoice(display) is { } choice)
        {
            action = string.Format(CultureInfo.InvariantCulture, Resources.HtmlMenuChoice, choice);
        }

        if ((key.Modifiers & ConsoleModifiers.Control) != 0)
        {
            action = string.Format(CultureInfo.InvariantCulture, Resources.HtmlControlKey, action);
        }

        if ((key.Modifiers & ConsoleModifiers.Alt) != 0)
        {
            action = string.Format(CultureInfo.InvariantCulture, Resources.HtmlAltKey, action);
        }

        if ((key.Modifiers & ConsoleModifiers.Shift) != 0)
        {
            action = string.Format(CultureInfo.InvariantCulture, Resources.HtmlShiftKey, action);
        }

        return action;
    }

    private static string? NumericChoice(Display display)
    {
        var editable = display.InputFields.Where(field => !field.Bypass).ToArray();
        var modified = editable.Where(field => field.Modified).ToArray();
        if (modified.Length > 1 || modified.Length == 0 && editable.Length != 1)
            return null;

        var field = modified.Length == 1 ? modified[0] : editable[0];
        if (field.Transparent)
            return null;
        byte attribute = _normalAttribute;
        for (var i = 0; i < field.Start; i++)
        {
            var value = display.Data[i];
            if ((value & _attributeMask) == _normalAttribute)
            {
                attribute = value;
            }
        }

        if ((attribute & _nonDisplayMask) == _nonDisplayMask)
            return null;

        var text = new StringBuilder(field.Length);
        for (var i = field.Start; i < field.End; i++)
        {
            text.Append(display.Character(display.Data[i]));
        }

        var choice = text.ToString().Trim();
        return choice.Length > 0 && choice.All(character => character is >= '0' and <= '9') ? choice : null;
    }

    internal string? Save(Display display, bool beforeLeaving = false)
    {
        if (display._screenRevision == 0 || !beforeLeaving && display._screenRevision == _lastRevision)
            return null;

        var screen = BuildScreen(display);
        if (string.Equals(screen, _lastScreen, StringComparison.Ordinal))
        {
            _lastRevision = display._screenRevision;
            return null;
        }

        var number = checked(_screenNumber + 1);
        var captured = DateTimeOffset.UtcNow;
        var name = string.Format(CultureInfo.InvariantCulture, _fileNameFormat, Environment.ProcessId, number, captured);
        var path = Path.Combine(_directory, name);
        var reason = beforeLeaving ? Resources.HtmlBeforeLeaving
            : _frames.Count == 0 ? Resources.HtmlStartup : Resources.HtmlReloaded;
        var action = _action ?? Resources.HtmlHostUpdate;
        var document = BuildDocument(screen, number, captured, reason, action);
        Directory.CreateDirectory(_directory);
        WriteDocument(path, document, false);
        if (_lastPath != null && _lastDocument != null)
        {
            var updated = _lastDocument.Replace(_nextPlaceholder, BuildLink(name, _nextRelation, Resources.HtmlNext), StringComparison.Ordinal);
            WriteDocument(_lastPath, updated, true);
        }

        _frames.Add(new CapturedFrame(name, number, captured, display.Rows, display.Columns, reason, action));
        WriteDocument(_indexPath, BuildIndex(), true);
        _lastPath = path;
        _lastDocument = document;
        _lastScreen = screen;
        _lastRevision = display._screenRevision;
        _screenNumber = number;
        return path;
    }

    private StringBuilder CreateDocument(bool index = false)
    {
        var title = string.Format(CultureInfo.InvariantCulture, Resources.ConsoleTitle, _host);
        var builder = new StringBuilder(_documentStart);
        builder.Append(_encoder.Encode(title));
        builder.Append(_documentStyle);
        for (var attribute = _normalAttribute; attribute < _attributeLimit; attribute++)
        {
            var color = _colors[((attribute & 0x10) >> 2) | ((attribute & 8) >> 2) | ((attribute & 2) >> 1)];
            var hidden = (attribute & _nonDisplayMask) == _nonDisplayMask;
            var reverse = !hidden && (attribute & 1) != 0;
            builder.Append(_styleClassPrefix);
            builder.Append(attribute.ToString("X2", CultureInfo.InvariantCulture));
            builder.Append(_colorProperty);
            builder.Append(reverse ? _black : color);
            builder.Append(_backgroundProperty);
            builder.Append(reverse ? color : _black);
            if (!hidden && (attribute & _underlineMask) != 0)
            {
                builder.Append(_underlineProperty);
            }

            builder.Append(_styleClose);
            builder.Append(_lineEnding);
        }

        builder.Append(index ? _indexStyleEnd : _styleEnd);
        return builder;
    }

    private string BuildDocument(string screen, int number, DateTimeOffset captured, string reason, string action)
    {
        var builder = CreateDocument();
        builder.Append(screen);
        builder.Append(_footerOpen);
        builder.Append(_encoder.Encode(Resources.HtmlFooterTitle));
        builder.Append(_headingClose);
        AppendFact(builder, Resources.HtmlSession, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        AppendFact(builder, Resources.HtmlScreen, number.ToString(_numberFormat, CultureInfo.InvariantCulture));
        AppendFact(builder, Resources.HtmlCaptured, captured.ToString(_timestampFormat, CultureInfo.InvariantCulture));
        AppendFact(builder, Resources.HtmlCaptureReason, reason);
        builder.AppendFormat(CultureInfo.InvariantCulture, _actionFactOpen, _encoder.Encode(Resources.HtmlAction));
        AppendAction(builder, action);
        builder.Append(_actionFactClose);
        builder.Append(_connectionDetails);
        builder.Append(_detailsClose);
        AppendArguments(builder);
        builder.Append(_navigationOpen);
        if (_lastPath != null)
        {
            builder.Append(BuildLink(Path.GetFileName(_lastPath), _previousRelation, Resources.HtmlPrevious));
        }

        builder.Append(BuildLink(_indexName, _indexRelation, Resources.HtmlIndex));
        builder.Append(_nextPlaceholder);
        builder.Append(_navigationClose);
        builder.Append(_footerClose);
        builder.Append(_documentEnd);
        builder.Append(_lineEnding);
        return builder.ToString().ReplaceLineEndings(_lineEnding);
    }

    private string BuildIndex()
    {
        var builder = CreateDocument(true);
        builder.Append(_indexHeaderOpen);
        builder.Append(_encoder.Encode(Resources.HtmlIndexTitle));
        builder.Append(_headingClose);
        AppendFact(builder, Resources.HtmlSession, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        AppendFact(builder, Resources.HtmlSessionStarted, _sessionStarted.ToString(_timestampFormat, CultureInfo.InvariantCulture));
        AppendFact(builder, Resources.HtmlScreenCount, _frames.Count.ToString(CultureInfo.InvariantCulture));
        builder.Append(_connectionDetails);
        builder.Append(_detailsClose);
        AppendArguments(builder);
        builder.Append(_indexLinksOpen);
        foreach (var frame in _frames)
        {
            var label = GetFrameLabel(frame);
            builder.AppendFormat(CultureInfo.InvariantCulture, _indexLinkFormat,
                _encoder.Encode(Uri.EscapeDataString(frame.Name)), _encoder.Encode(label));
        }

        builder.Append(_indexLinksClose);
        builder.Append(_indexHeaderClose);
        foreach (var frame in _frames)
        {
            var label = GetFrameLabel(frame);
            var name = _encoder.Encode(Uri.EscapeDataString(frame.Name));
            var title = _encoder.Encode(label);
            builder.AppendFormat(CultureInfo.InvariantCulture, _frameHeadingFormat, name, title);
            AppendAction(builder, frame.Action);
            builder.AppendFormat(CultureInfo.InvariantCulture, _frameOpenFormat, name, title, frame.Columns, frame.Rows);
            builder.Append(_lineEnding);
        }

        builder.Append(_documentEnd);
        builder.Append(_lineEnding);
        return builder.ToString().ReplaceLineEndings(_lineEnding);
    }

    private static string GetFrameLabel(CapturedFrame frame)
    {
        return string.Format(CultureInfo.InvariantCulture, Resources.HtmlFrameLabel,
            frame.Number.ToString(_numberFormat, CultureInfo.InvariantCulture),
            frame.Captured.ToString(_timestampFormat, CultureInfo.InvariantCulture), frame.Reason, frame.Action);
    }

    private static void AppendAction(StringBuilder builder, string action)
    {
        builder.AppendFormat(CultureInfo.InvariantCulture, _actionOpen, _encoder.Encode(action));
        if (action == Resources.HtmlHostUpdate)
        {
            builder.Append(_encoder.Encode(action));
        }
        else
        {
            var keys = action;
            var placeholder = Resources.HtmlMenuChoice.IndexOf(_choicePlaceholder, StringComparison.Ordinal);
            var prefix = Resources.HtmlMenuChoice[..placeholder];
            keys = keys.Replace(prefix, string.Empty, StringComparison.Ordinal);
            var parts = keys.Split([','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                if (i != 0)
                {
                    builder.Append(_actionSeparator);
                }

                var chord = parts[i].Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                for (var j = 0; j < chord.Length; j++)
                {
                    if (j != 0)
                    {
                        builder.Append(_modifierSeparator);
                    }

                    builder.AppendFormat(CultureInfo.InvariantCulture, _keyFormat, _encoder.Encode(chord[j]));
                }
            }
        }

        builder.Append(_actionClose);
    }

    private void AppendArguments(StringBuilder builder)
    {
        builder.Append(_argumentsOpen);
        builder.Append(_encoder.Encode(Resources.HtmlArguments));
        builder.Append(_argumentsValue);
        builder.Append(_encoder.Encode(_arguments));
        builder.Append(_argumentsClose);
    }

    private static void AppendFact(StringBuilder builder, string label, string value)
    {
        builder.AppendFormat(CultureInfo.InvariantCulture, _factFormat, _encoder.Encode(label), _encoder.Encode(value));
        builder.Append(_lineEnding);
    }

    private static string BuildLink(string name, string relation, string label)
    {
        return string.Format(CultureInfo.InvariantCulture, _linkFormat,
            relation, _encoder.Encode(Uri.EscapeDataString(name)), _encoder.Encode(label));
    }

    private static void WriteDocument(string path, string document, bool overwrite)
    {
        var temporary = path + Guid.NewGuid().ToString("N") + _temporaryExtension;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, _utf8);
                writer.Write(document);
            }

            File.Move(temporary, path, overwrite);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string BuildScreen(Display display)
    {
        var builder = new StringBuilder();
        builder.AppendFormat(CultureInfo.InvariantCulture, _screenOpen, display.Rows, display.Columns);
        byte attribute = _normalAttribute;
        builder.AppendFormat(CultureInfo.InvariantCulture, _spanOpen, attribute);
        for (var row = 0; row < display.Rows; row++)
        {
            if (row != 0)
            {
                builder.Append(_spanClose);
                builder.Append(_lineEnding);
                builder.AppendFormat(CultureInfo.InvariantCulture, _spanOpen, attribute);
            }

            for (var column = 0; column < display.Columns; column++)
            {
                var value = display.Data[row * display.Columns + column];
                if ((value & _attributeMask) == _normalAttribute)
                {
                    builder.Append(' ');
                    builder.Append(_spanClose);
                    attribute = value;
                    builder.AppendFormat(CultureInfo.InvariantCulture, _spanOpen, attribute);
                }
                else if ((attribute & _nonDisplayMask) == _nonDisplayMask)
                {
                    builder.Append(' ');
                }
                else
                {
                    builder.Append(_encoder.Encode(display.Character(value).ToString()));
                }
            }
        }

        builder.Append(_screenClose);
        return builder.ToString();
    }

    private sealed record CapturedFrame(string Name, int Number, DateTimeOffset Captured, int Rows, int Columns, string Reason, string Action);
}
