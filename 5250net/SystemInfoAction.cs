namespace Terminal5250;

internal sealed class SystemInfoAction : SynchronousCommandLineAction
{
    public override bool Terminating => true;
    public override bool ClearsParseErrors => true;

    public override int Invoke(ParseResult parseResult)
    {
        var output = parseResult.InvocationConfiguration.Output;
        foreach (var item in SystemInformation.GetValues())
        {
            output.WriteLine(string.Format(CultureInfo.CurrentCulture, Resources.InfoItem, item.Key, item.Value));
        }

        return 0;
    }
}
