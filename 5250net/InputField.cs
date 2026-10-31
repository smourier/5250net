namespace Terminal5250;

internal sealed class InputField(int start, int length, byte format1, byte format2, bool transparent)
{
    internal int _start { get; } = start;
    internal int _length { get; } = length;
    internal byte _format1 { get; set; } = format1;
    internal byte _format2 { get; set; } = format2;
    internal bool _transparent { get; } = transparent;
    internal bool _modified { get; set; } = (format1 & 8) != 0;
    internal bool _exited { get; set; }
    internal bool _bypass => (_format1 & 0x20) != 0;
    internal int _end => _start + _length;
}
