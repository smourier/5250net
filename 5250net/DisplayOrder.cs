namespace Terminal5250;

internal enum DisplayOrder : byte
{
    StartOfHeader = 0x01,
    RepeatToAddress = 0x02,
    TransparentData = 0x10,
    SetBufferAddress = 0x11,
    InsertCursor = 0x13,
    MoveCursor = 0x14,
    StartOfField = 0x1D,
}
