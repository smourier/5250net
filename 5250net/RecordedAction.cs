namespace Terminal5250;

internal sealed record RecordedAction(ConsoleKeyInfo? Key, long Delay, bool Submitted, bool ResultError);
