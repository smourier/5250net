namespace Terminal5250;

internal sealed class InputField(int start, int length, byte format1, byte format2, bool transparent)
{
    internal int Start { get; } = start;
    internal int Length { get; } = length;
    internal byte Format1 { get; set; } = format1;
    internal byte Format2 { get; set; } = format2;
    internal bool Transparent { get; } = transparent;
    internal bool Modified { get; set; } = (format1 & 8) != 0;
    internal bool Exited { get; set; }
    internal bool Bypass => (Format1 & 0x20) != 0;
    internal int End => Start + Length;
}
