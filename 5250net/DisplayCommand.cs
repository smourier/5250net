namespace Terminal5250;

internal enum DisplayCommand : byte
{
    Save = 0x02,
    WriteToDisplay = 0x11,
    RestoreScreen = 0x12,
    ClearUnitAlternate = 0x20,
    WriteErrorCode = 0x21,
    Roll = 0x23,
    ClearUnit = 0x40,
    ReadInputFields = 0x42,
    ClearFormatTable = 0x50,
    ReadMdtFields = 0x52,
    ReadScreen = 0x62,
    ReadImmediate = 0x72,
    ReadMdtFieldsAlternate = 0x82,
    ReadMdtImmediateAlternate = 0x83,
    WriteStructuredField = 0xF3,
}
