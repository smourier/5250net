namespace Terminal5250;

internal sealed class Display(Encoding encoding, bool wide)
{
    private const int _maximumFields = 256;
    private const int _narrowRows = 24;
    private const int _narrowColumns = 80;
    private const int _wideRows = 27;
    private const int _wideColumns = 132;
    private const byte _escape = 0x04;
    private const byte _blank = 0x40;
    private const byte _normalAttribute = 0x20;
    private const byte _attributeMask = 0xE0;
    private const byte _nonDisplayMask = 7;
    private const byte _formatMask = 0xC0;
    private const byte _formatMark = 0x40;
    private const byte _controlMark = 0x80;
    private const byte _transparentControl = 0x84;
    private const int _functionKeyCount = 12;
    private const byte _firstFunctionAid = 0x31;
    private const byte _lastFunctionAid = 0x3C;
    private const byte _firstShiftFunctionAid = 0xB1;
    private const byte _lastShiftFunctionAid = 0xBC;
    private const byte _clearAid = 0xBD;
    private const byte _enterAid = 0xF1;
    private const byte _helpAid = 0xF3;
    private const byte _pageUpAid = 0xF4;
    private const byte _pageDownAid = 0xF5;
    private const byte _printAid = 0xF6;
    private const byte _recordBackspaceAid = 0xF8;
    private const byte _structuredFieldAid = 0x88;
    private const string _wideDeviceModel = "3477002";
    private const string _narrowDeviceModel = "3179002";
    private const string _minusSign = "-";
    private const string _space = " ";
    private byte[] _cells = new byte[_narrowRows * _narrowColumns];
    private readonly List<InputField> _fields = [];
    private byte[] _header = [];
    private int _address;
    private int _insertCursor;
    private byte _readCommand;
    private byte _readControl1;
    private byte _readControl2;
    private bool _insert;
    private bool _message;
    private int _previousFieldEnd = -1;
    private bool _explicitAddress;
    private byte[]? _errorLine;
    private int _errorLineStart;

    internal bool _messageWaiting => _message;
    internal bool _insertMode => _insert;

    internal int _rows { get; private set; } = _narrowRows;
    internal int _columns { get; private set; } = _narrowColumns;
    internal int _cursor { get; private set; }
    internal bool _locked { get; private set; } = true;
    internal bool _inputError { get; private set; }
    internal ReadOnlySpan<byte> _data => _cells;
    internal IReadOnlyList<InputField> _inputFields => _fields;

    internal List<byte[]> Apply(byte[] record)
    {
        if (StartupResponse.Handle(record))
            return [];

        var offset = GdsRecord.Validate(record);
        var operation = record[6 + record[6] - 1];
        var replies = new List<byte[]>();
        switch (operation)
        {
            case 0:
            case 1:
            case 2:
            case 3:
            case 4:
            case 5:
            case 6:
            case 8:
                break;

            case 10:
                _readCommand = 0;
                _locked = true;
                replies.Add(GdsRecord.Create([], 10));
                return replies;

            case 11:
            case 12:
                _message = operation == 11;
                return replies;

            default:
                throw new InvalidDataException(string.Format(CultureInfo.InvariantCulture, Resources.UnknownOperation, operation));
        }

        var data = record.AsSpan(offset);
        var i = 0;
        while (i < data.Length)
        {
            if (Take(data, ref i) != _escape)
                throw new InvalidDataException(Resources.BadRecord);

            var command = Take(data, ref i);
            switch ((DisplayCommand)command)
            {
                case DisplayCommand.ClearUnit:
                    Clear(false);
                    break;

                case DisplayCommand.ClearUnitAlternate:
                    if (!wide || Take(data, ref i) is not 0 and not _blank)
                        throw new InvalidDataException(Resources.BadRecord);

                    Clear(true);
                    break;

                case DisplayCommand.ClearFormatTable:
                    _fields.Clear();
                    _header = [];
                    _readCommand = 0;
                    _insertCursor = 0;
                    _insert = false;
                    _locked = true;
                    break;

                case DisplayCommand.Roll:
                    Roll(data, ref i);
                    break;

                case DisplayCommand.WriteErrorCode:
                    WriteError(data, ref i);
                    break;

                case DisplayCommand.WriteToDisplay:
                    WriteDisplay(data, ref i);
                    break;

                case DisplayCommand.ReadInputFields:
                case DisplayCommand.ReadMdtFields:
                case DisplayCommand.ReadMdtFieldsAlternate:
                    _readCommand = command;
                    _readControl1 = Take(data, ref i);
                    _readControl2 = Take(data, ref i);
                    break;

                case DisplayCommand.Save:
                    replies.Add(GdsRecord.Create(Save(), 4));
                    break;

                case DisplayCommand.RestoreScreen:
                    Restore(data[i..]);
                    i = data.Length;
                    break;

                case DisplayCommand.ReadImmediate:
                case DisplayCommand.ReadMdtImmediateAlternate:
                    replies.Add(GdsRecord.Create(InputData(0, command == 0x72 ? (byte)0x42 : (byte)0x82)));
                    break;

                case DisplayCommand.ReadScreen:
                    replies.Add(GdsRecord.Create(_cells));
                    break;

                case DisplayCommand.WriteStructuredField:
                    var length = Word(data, ref i);
                    if (length != 5 || Take(data, ref i) != 0xD9 || Take(data, ref i) != 0x70 || Take(data, ref i) != 0)
                        throw new InvalidDataException(Resources.BadRecord);

                    replies.Add(Query());
                    break;

                default:
                    throw new InvalidDataException(string.Format(CultureInfo.InvariantCulture, Resources.UnsupportedCommand, command));
            }
        }

        return replies;
    }

    private void Roll(ReadOnlySpan<byte> data, ref int i)
    {
        var parameter = Take(data, ref i);
        var top = Take(data, ref i);
        var bottom = Take(data, ref i);
        var lines = parameter & 0x1F;
        if (top == 0 || bottom > _rows || top >= bottom || lines > bottom - top)
            throw new InvalidDataException(Resources.BadAddress);

        var start = (top - 1) * _columns;
        var shift = lines * _columns;
        var count = (bottom - top + 1) * _columns - shift;
        if ((parameter & 0x80) != 0)
        {
            Array.Copy(_cells, start, _cells, start + shift, count);
        }
        else
        {
            Array.Copy(_cells, start + shift, _cells, start, count);
        }
    }

    private void WriteError(ReadOnlySpan<byte> data, ref int i)
    {
        var row = _header.Length > 3 && _header[3] > 0 && _header[3] <= _rows ? _header[3] : _rows;
        _errorLineStart = (row - 1) * _columns;
        _errorLine = _cells.AsSpan(_errorLineStart, _columns).ToArray();
        var column = 0;
        while (i < data.Length && data[i] != _escape)
        {
            var value = Take(data, ref i);
            if (value == 0x13)
            {
                _cursor = Address(data, ref i);
                continue;
            }

            if (column >= _columns)
                throw new InvalidDataException(Resources.BadRecord);

            _cells[_errorLineStart + column++] = value;
        }

        _inputError = true;
        _locked = false;
        _insert = false;
    }

    private void Clear(bool alternate)
    {
        _columns = alternate ? _wideColumns : _narrowColumns;
        _rows = alternate ? _wideRows : _narrowRows;
        _cells = new byte[_rows * _columns];
        _cells[0] = _normalAttribute;
        _fields.Clear();
        _header = [];
        _cursor = 0;
        _insertCursor = 0;
        _address = 0;
        _readCommand = 0;
        _locked = true;
        _inputError = false;
        _errorLine = null;
        _insert = false;
    }

    private void WriteDisplay(ReadOnlySpan<byte> data, ref int i)
    {
        var cc1 = Take(data, ref i);
        var cc2 = Take(data, ref i);
        Control1(cc1);
        _address = 0;
        _previousFieldEnd = -1;
        _explicitAddress = false;
        while (i < data.Length && data[i] != _escape)
        {
            var order = Take(data, ref i);
            switch ((DisplayOrder)order)
            {
                case DisplayOrder.StartOfHeader:
                    var count = Take(data, ref i);
                    if (count == 0 || count == 255 || i + count > data.Length)
                        throw new InvalidDataException(Resources.BadRecord);

                    _header = data.Slice(i, count).ToArray();
                    i += count;
                    if (_header.Length > 2 && _header[2] != 0)
                        throw new InvalidDataException(string.Format(CultureInfo.InvariantCulture, Resources.UnsupportedCommand, order));

                    _fields.Clear();
                    break;

                case DisplayOrder.RepeatToAddress:
                    var end = Address(data, ref i);
                    var repeated = Take(data, ref i);
                    if (_address < 0 || end < _address)
                        throw new InvalidDataException(Resources.BadAddress);

                    while (_address <= end)
                    {
                        _cells[_address++] = repeated;
                    }

                    _address %= _cells.Length;
                    break;

                case DisplayOrder.TransparentData:
                    var size = Word(data, ref i);
                    if (i + size > data.Length || _address < 0 || _address + size > _cells.Length)
                        throw new InvalidDataException(Resources.BadRecord);

                    data.Slice(i, size).CopyTo(_cells.AsSpan(_address));
                    i += size;
                    _address = (_address + size) % _cells.Length;
                    break;

                case DisplayOrder.SetBufferAddress:
                    _address = Address(data, ref i, true);
                    _explicitAddress = true;
                    break;

                case DisplayOrder.InsertCursor:
                case DisplayOrder.MoveCursor:
                    _insertCursor = Address(data, ref i);
                    _cursor = _insertCursor;
                    break;

                case DisplayOrder.StartOfField:
                    StartField(data, ref i);
                    break;

                default:
                    if (order is > 0 and < 0x20)
                        throw new InvalidDataException(string.Format(CultureInfo.InvariantCulture, Resources.UnsupportedCommand, order));

                    if (_address < 0)
                        throw new InvalidDataException(Resources.BadAddress);

                    _cells[_address] = order;
                    _address = (_address + 1) % _cells.Length;
                    break;
            }
        }

        Control2(cc2);
    }

    private void StartField(ReadOnlySpan<byte> data, ref int i)
    {
        byte first = 0;
        byte second = 0;
        var input = i < data.Length && (data[i] & _formatMask) == _formatMark;
        if (input)
        {
            first = Take(data, ref i);
            second = Take(data, ref i);
        }

        var transparent = false;
        while (i < data.Length && (data[i] & _formatMask) == _controlMark)
        {
            var fcw = Take(data, ref i);
            _ = Take(data, ref i);
            if (fcw != _transparentControl)
                throw new InvalidDataException(string.Format(CultureInfo.InvariantCulture, Resources.UnsupportedCommand, fcw));

            transparent = true;
        }

        var attribute = Take(data, ref i);
        var length = Word(data, ref i);
        if ((attribute & _attributeMask) != _normalAttribute)
            throw new InvalidDataException(Resources.BadField);

        if (!_explicitAddress && _previousFieldEnd >= 0)
        {
            _address = _previousFieldEnd % _cells.Length;
        }

        if (_address == -1 && (attribute & _nonDisplayMask) == _nonDisplayMask)
            throw new InvalidDataException(Resources.BadField);

        var start = _address + 1;
        var existing = _fields.Find(f => f._start == start);
        if (existing != null && input)
        {
            existing._format1 = first;
            existing._format2 = second;
            existing._modified = (first & 8) != 0;
            length = existing._length;
        }
        else
        {
            if (length == 0 || (first & 7) == 7 && length < 2 || start + length > _cells.Length || _fields.Count >= _maximumFields ||
                input && _fields.Any(f => start < f._end && start + length > f._start))
                throw new InvalidDataException(Resources.BadField);

            if (input)
            {
                _fields.Add(new InputField(start, length, first, second, transparent));
                _fields.Sort((left, right) => left._start.CompareTo(right._start));
            }

            _cells[(start + length) % _cells.Length] = _normalAttribute;
        }

        if (_address >= 0)
        {
            _cells[_address] = attribute;
        }

        _address = start;
        _previousFieldEnd = start + length;
        _explicitAddress = false;
        if (input)
        {
            _locked = true;
        }
    }

    private void Control1(byte control)
    {
        var action = control >> 5;
        if (action != 0)
        {
            _locked = true;
        }

        foreach (var field in _fields)
        {
            if (!field._bypass && (action is 5 or 7 || action is 4 or 6 && field._modified))
            {
                _cells.AsSpan(field._start, field._length).Clear();
            }

            if (action is 3 or 7 || !field._bypass && action is 2 or 5 or 6)
            {
                field._modified = false;
            }
        }
    }

    private void Control2(byte control)
    {
        if ((control & 8) != 0)
        {
            if (_locked && (control & 0x40) == 0)
            {
                _cursor = _insertCursor;
            }

            _locked = false;
            _inputError = false;
        }

        if ((control & 2) != 0)
        {
            _message = false;
        }

        if ((control & 1) != 0)
        {
            _message = true;
        }
    }

    private byte[] Save()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(_escape);
        writer.Write((byte)0x12);
        writer.Write(_rows);
        writer.Write(_columns);
        writer.Write(_cursor);
        writer.Write(_insertCursor);
        writer.Write(_locked);
        writer.Write(_insert);
        writer.Write(_inputError);
        writer.Write(_errorLineStart);
        writer.Write(_errorLine?.Length ?? 0);
        if (_errorLine != null)
        {
            writer.Write(_errorLine);
        }

        writer.Write(_readCommand);
        writer.Write(_readControl1);
        writer.Write(_readControl2);
        writer.Write(_header.Length);
        writer.Write(_header);
        writer.Write(_cells);
        writer.Write(_fields.Count);
        foreach (var field in _fields)
        {
            writer.Write(field._start);
            writer.Write(field._length);
            writer.Write(field._format1);
            writer.Write(field._format2);
            writer.Write(field._transparent);
            writer.Write(field._modified);
            writer.Write(field._exited);
        }

        return stream.ToArray();
    }

    private void Restore(ReadOnlySpan<byte> data)
    {
        using var stream = new MemoryStream(data.ToArray());
        using var reader = new BinaryReader(stream);
        var rows = reader.ReadInt32();
        var columns = reader.ReadInt32();
        if ((rows != _narrowRows || columns != _narrowColumns) && (!wide || rows != _wideRows || columns != _wideColumns))
            throw new InvalidDataException(Resources.BadRecord);

        var cursor = reader.ReadInt32();
        var insertCursor = reader.ReadInt32();
        if (cursor < 0 || cursor >= rows * columns || insertCursor < 0 || insertCursor >= rows * columns)
            throw new InvalidDataException(Resources.BadAddress);

        var locked = reader.ReadBoolean();
        var insert = reader.ReadBoolean();
        var inputError = reader.ReadBoolean();
        var errorStart = reader.ReadInt32();
        var errorLength = reader.ReadInt32();
        if (errorLength != 0 && errorLength != columns || errorStart < 0 || errorStart > rows * columns - errorLength)
            throw new InvalidDataException(Resources.BadRecord);

        var errorLine = reader.ReadBytes(errorLength);
        var read = reader.ReadByte();
        var control1 = reader.ReadByte();
        var control2 = reader.ReadByte();
        var headerLength = reader.ReadInt32();
        if (headerLength is < 0 or > 254)
            throw new InvalidDataException(Resources.BadRecord);

        var header = reader.ReadBytes(headerLength);
        var cells = reader.ReadBytes(rows * columns);
        var count = reader.ReadInt32();
        if (header.Length != headerLength || cells.Length != rows * columns || count is < 0 or > _maximumFields)
            throw new InvalidDataException(Resources.BadRecord);

        var fields = new List<InputField>();
        for (var i = 0; i < count; i++)
        {
            var start = reader.ReadInt32();
            var length = reader.ReadInt32();
            if (start < 0 || length <= 0 || start > cells.Length - length || fields.Any(f => start < f._end && start + length > f._start))
                throw new InvalidDataException(Resources.BadField);

            var field = new InputField(start, length, reader.ReadByte(), reader.ReadByte(), reader.ReadBoolean())
            {
                _modified = reader.ReadBoolean(),
                _exited = reader.ReadBoolean(),
            };

            fields.Add(field);
        }

        if (stream.Position != stream.Length)
            throw new InvalidDataException(Resources.BadRecord);

        _rows = rows;
        _columns = columns;
        _cursor = cursor;
        _insertCursor = insertCursor;
        _locked = locked;
        _insert = insert;
        _inputError = inputError;
        _errorLine = errorLength == 0 ? null : errorLine;
        _errorLineStart = errorStart;
        _readCommand = read;
        _readControl1 = control1;
        _readControl2 = control2;
        _header = header;
        _cells = cells;
        _fields.Clear();
        _fields.AddRange(fields);
    }

    private byte[] Query()
    {
        var payload = new byte[61];
        payload[2] = _structuredFieldAid;
        payload[4] = 0x3A;
        payload[5] = 0xD9;
        payload[6] = 0x70;
        payload[7] = 0x80;
        payload[8] = 6;
        payload[10] = 1;
        payload[29] = 1;
        encoding.GetBytes(wide ? _wideDeviceModel : _narrowDeviceModel).CopyTo(payload, 30);
        payload[37] = 2;
        payload[44] = 1;
        payload[49] = 0x63;
        payload[50] = wide ? (byte)_firstFunctionAid : (byte)0x11;
        return GdsRecord.Create(payload);
    }

    private int Address(ReadOnlySpan<byte> data, ref int i, bool allowZero = false)
    {
        var row = Take(data, ref i);
        var column = Take(data, ref i);
        if (allowZero && row == 1 && column == 0)
            return -1;

        if (row == 0 || row > _rows || column == 0 || column > _columns)
            throw new InvalidDataException(Resources.BadAddress);

        return (row - 1) * _columns + column - 1;
    }

    private static byte Take(ReadOnlySpan<byte> data, ref int i)
    {
        if (i >= data.Length)
            throw new InvalidDataException(Resources.BadRecord);

        return data[i++];
    }

    private static int Word(ReadOnlySpan<byte> data, ref int i)
    {
        var high = Take(data, ref i);
        return high * 256 + Take(data, ref i);
    }

    internal byte[]? Key(ConsoleKeyInfo key)
    {
        var control = (key.Modifiers & ConsoleModifiers.Control) != 0;
        var shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;
        if (control && key.Key is ConsoleKey.A or ConsoleKey.S)
            return GdsRecord.Create([], flags: key.Key == ConsoleKey.A ? (byte)0x40 : (byte)4);

        if (control && key.Key == ConsoleKey.Enter && !_locked && !_inputError)
        {
            var current = CurrentField();
            if (current == null || current._bypass)
            {
                _inputError = true;
                return null;
            }

            _cells.AsSpan(_cursor, current._end - _cursor).Clear();
            current._modified = true;
            if (FinishField(current, true))
            {
                Tab(false);
                if ((current._format2 & 0x80) != 0)
                    return Submit(_enterAid);
            }

            return null;
        }

        if (key.Key == ConsoleKey.Escape)
        {
            if (_errorLine != null)
            {
                _errorLine.CopyTo(_cells, _errorLineStart);
                _errorLine = null;
            }

            _inputError = false;
            return null;
        }

        if (_inputError && key.Key == ConsoleKey.H && (key.Modifiers & ConsoleModifiers.Alt) != 0 && _errorLine != null)
        {
            var error = new byte[2];
            for (var i = 0; i < 4; i++)
            {
                error[i / 2] |= (byte)((_cells[_errorLineStart + i + 1] & 0x0F) << (i % 2 == 0 ? 4 : 0));
            }

            return GdsRecord.Create(error, flags: 1);
        }

        if (_locked || _inputError)
            return null;

        byte aid = key.Key switch
        {
            >= ConsoleKey.F1 and <= ConsoleKey.F12 => (byte)((shift ? _firstShiftFunctionAid : _firstFunctionAid) + key.Key - ConsoleKey.F1),
            ConsoleKey.Enter => _enterAid,
            ConsoleKey.PageUp => _pageUpAid,
            ConsoleKey.PageDown => _pageDownAid,
            ConsoleKey.H when (key.Modifiers & ConsoleModifiers.Alt) != 0 => _helpAid,
            _ => 0,
        };

        if (aid != 0)
            return Submit(aid);

        var field = CurrentField();
        switch (key.Key)
        {
            case ConsoleKey.Tab:
                if (field != null && !field._bypass && !FinishField(field, false))
                    break;

                Tab(shift);
                break;

            case ConsoleKey.LeftArrow:
                _cursor = (_cursor + _cells.Length - 1) % _cells.Length;
                break;

            case ConsoleKey.RightArrow:
                _cursor = (_cursor + 1) % _cells.Length;
                break;

            case ConsoleKey.UpArrow:
                _cursor = (_cursor + _cells.Length - _columns) % _cells.Length;
                break;

            case ConsoleKey.DownArrow:
                _cursor = (_cursor + _columns) % _cells.Length;
                break;

            case ConsoleKey.Home:
                _cursor = field?._start ?? _fields.FirstOrDefault(f => !f._bypass)?._start ?? 0;
                break;

            case ConsoleKey.End:
                if (field != null)
                {
                    if (control)
                    {
                        _cells.AsSpan(_cursor, field._end - _cursor).Clear();
                        field._modified = true;
                    }
                    else
                    {
                        var end = field._end - 1;
                        while (end > field._start && _cells[end] is 0 or _blank)
                        {
                            end--;
                        }

                        _cursor = Math.Min(end + 1, field._end - 1);
                    }
                }

                break;

            case ConsoleKey.Add:
            case ConsoleKey.Subtract:
                if (field == null || field._bypass)
                {
                    _inputError = true;
                    break;
                }

                _cells.AsSpan(_cursor, field._end - _cursor).Clear();
                field._modified = true;
                if ((field._format1 & 7) == 7)
                {
                    _cells[field._end - 1] = encoding.GetBytes(key.Key == ConsoleKey.Subtract ? _minusSign : _space)[0];
                }

                if (FinishField(field, true))
                {
                    Tab(false);
                }

                break;

            case ConsoleKey.Insert:
                _insert = !_insert;
                break;

            case ConsoleKey.Backspace:
            case ConsoleKey.Delete:
                if (field == null || field._bypass || key.Key == ConsoleKey.Backspace && _cursor == field._start)
                {
                    _inputError = true;
                    break;
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    _cursor--;
                }

                Array.Copy(_cells, _cursor + 1, _cells, _cursor, field._end - _cursor - 1);
                _cells[field._end - 1] = 0;
                field._modified = true;
                break;

            default:
                if (!control && !char.IsControl(key.KeyChar))
                {
                    var originalCursor = _cursor;
                    Type(field, key.KeyChar);
                    if (!_inputError && field != null && originalCursor == field._end - 1 && (field._format2 & 0x80) != 0)
                        return Submit(_enterAid);
                }

                break;
        }

        return null;
    }

    private InputField? CurrentField() => _fields.Find(f => _cursor >= f._start && _cursor < f._end);

    private void Type(InputField? field, char character)
    {
        if (field == null || field._bypass)
        {
            _inputError = true;
            return;
        }

        if ((field._format2 & 0x20) != 0)
        {
            character = char.ToUpperInvariant(character);
        }

        var mode = field._format1 & 7;
        if (mode == 6 || mode == 5 && !char.IsAsciiDigit(character) ||
            mode == 1 && !char.IsLetter(character) && character is not ' ' and not ',' and not '.' and not '-' ||
            mode is 3 or 7 && !char.IsAsciiDigit(character) && character is not ' ' and not '+' and not '-' and not '.' and not ',')
        {
            _inputError = true;
            return;
        }

        byte[] bytes;
        try
        {
            bytes = encoding.GetBytes(character.ToString());
        }
        catch (EncoderFallbackException)
        {
            _inputError = true;
            return;
        }

        if (bytes.Length != 1 || _insert && _cells[field._end - 1] is not 0 and not _blank)
        {
            _inputError = true;
            return;
        }

        if (_insert)
        {
            Array.Copy(_cells, _cursor, _cells, _cursor + 1, field._end - _cursor - 1);
        }

        _cells[_cursor] = bytes[0];
        field._modified = true;
        field._exited = false;
        if (_cursor + 1 < field._end)
        {
            _cursor++;
        }
        else if ((field._format2 & 0x40) == 0 && FinishField(field, false))
        {
            Tab(false);
        }
    }

    private void Tab(bool reverse)
    {
        var current = CurrentField();
        var candidates = _fields.Where(f => !f._bypass).ToArray();
        if (candidates.Length == 0)
            return;

        var field = reverse
            ? candidates.LastOrDefault(f => f._start < (current?._start ?? _cursor)) ?? candidates[^1]
            : candidates.FirstOrDefault(f => f._start > _cursor) ?? candidates[0];
        _cursor = field._start;
    }

    private bool FinishField(InputField field, bool exit)
    {
        var adjustment = field._format2 & 7;
        if (field._modified && adjustment == 7 && _cells.AsSpan(field._start, field._length).Contains((byte)0))
        {
            _inputError = true;
            return false;
        }

        if (field._modified && adjustment is 5 or 6)
        {
            var length = field._length - ((field._format1 & 7) == 7 ? 1 : 0);
            var end = field._start + length;
            while (end > field._start && _cells[end - 1] is 0 or _blank)
            {
                end--;
            }

            var count = end - field._start;
            var padding = length - count;
            Array.Copy(_cells, field._start, _cells, field._start + padding, count);
            _cells.AsSpan(field._start, padding).Fill(adjustment == 5 ? (byte)0xF0 : (byte)0x40);
        }

        if (exit)
        {
            field._exited = true;
        }

        return true;
    }

    private byte[]? Submit(byte aid)
    {
        if (_readCommand == 0)
            return null;

        if (aid == _enterAid && _fields.Any(f => !f._bypass &&
            ((f._format2 & 8) != 0 && !f._modified || (f._format2 & 0x40) != 0 && f._modified && !f._exited)))
        {
            _inputError = true;
            return null;
        }

        if (aid == _enterAid && _fields.Any(f => !f._bypass && !FinishField(f, false)))
            return null;

        var data = InputData(aid, _readCommand);
        _readCommand = 0;
        _locked = true;
        Control1(_readControl1);
        Control2(_readControl2);
        return GdsRecord.Create(data);
    }

    private byte[] InputData(byte aid, byte read)
    {
        List<byte> data = [(byte)(_cursor / _columns + 1), (byte)(_cursor % _columns + 1), aid];
        var function = aid switch
        {
            >= _firstFunctionAid and <= _lastFunctionAid => aid - _firstFunctionAid + 1,
            >= _firstShiftFunctionAid and <= _lastShiftFunctionAid => aid - _firstShiftFunctionAid + 1 + _functionKeyCount,
            _ => 0,
        };

        if (function != 0)
        {
            var index = 6 - (function - 1) / 8;
            if (_header.Length >= 7 && (_header[index] & (1 << ((function - 1) % 8))) != 0)
                return [.. data];
        }

        if (aid is _clearAid or _helpAid or _printAid or _recordBackspaceAid || !_fields.Any(f => f._modified))
            return [.. data];

        foreach (var field in _fields)
        {
            if (read != 0x42 && !field._modified)
                continue;

            if (read != 0x42)
            {
                data.AddRange([0x11, (byte)(field._start / _columns + 1), (byte)(field._start % _columns + 1)]);
            }

            var value = _cells.AsSpan(field._start, field._length).ToArray();
            var length = value.Length;
            if (!field._transparent)
            {
                if (read != 0x42)
                {
                    while (length > 0 && value[length - 1] == 0)
                    {
                        length--;
                    }
                }

                if (read != 0x82)
                {
                    for (var i = 0; i < length; i++)
                    {
                        if (value[i] == 0)
                        {
                            value[i] = _blank;
                        }
                    }
                }
            }

            if (!field._transparent && (field._format1 & 7) == 7 && length > 1)
            {
                if (value[field._length - 1] == encoding.GetBytes(_minusSign)[0])
                {
                    value[field._length - 2] = (byte)(0xD0 | (value[field._length - 2] & 0x0F));
                }

                length = Math.Min(length, field._length - 1);
            }

            data.AddRange(value.AsSpan(0, length));
        }

        return [.. data];
    }

    internal char Character(byte value)
    {
        if (value == 0)
            return ' ';

        var character = encoding.GetString([value])[0];
        return char.IsControl(character) ? ' ' : character;
    }
}
