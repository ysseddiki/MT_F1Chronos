using System.Reflection;

namespace MT_F1Chronos.App;

public static class AppVersion
{
    public static string Display { get; } = Resolve();

    public static string Label => $"v{Display}";

    private static string Resolve()
    {
        var informational = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip any SourceLink suffix: "0.7.0+abcdef"
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        var version = typeof(AppVersion).Assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
