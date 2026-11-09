namespace SpireQoL.Diagnostics;

/// <summary>
/// Where error reports go. A Sentry DSN is meant to be public (it only allows sending events to
/// this one project), so it lives in source. Empty = reporting off in this build.
/// </summary>
public static class ReportingConfig
{
    public const string Dsn = "";
}
