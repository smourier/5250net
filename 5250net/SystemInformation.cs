namespace Terminal5250;

internal static class SystemInformation
{
    internal static IEnumerable<KeyValuePair<string, string>> GetValues()
    {
        var assembly = typeof(SystemInformation).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? Resources.HtmlUnknown;
        yield return new(Resources.HtmlApplicationVersion, version);
        yield return new(Resources.HtmlOperatingSystem, RuntimeInformation.OSDescription);
        yield return new(Resources.HtmlOperatingSystemVersion, Environment.OSVersion.VersionString);
        yield return new(Resources.HtmlOperatingSystemArchitecture, RuntimeInformation.OSArchitecture.ToString());
        yield return new(Resources.HtmlProcessArchitecture, RuntimeInformation.ProcessArchitecture.ToString());
        yield return new(Resources.HtmlRuntime, RuntimeInformation.FrameworkDescription);
        yield return new(Resources.HtmlTimeZone, TimeZoneInfo.Local.Id);
    }
}
