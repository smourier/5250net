namespace Terminal5250;

internal sealed class BannerHelpAction(HelpAction help) : SynchronousCommandLineAction
{
    public override bool Terminating => help.Terminating;
    public override bool ClearsParseErrors => help.ClearsParseErrors;

    public override int Invoke(ParseResult parseResult)
    {
        var output = parseResult.InvocationConfiguration.Output;
        output.WriteLine(ConsoleRenderer.BuildBanner(DateTime.Now.Year));
        output.WriteLine();
        return help.Invoke(parseResult);
    }
}
