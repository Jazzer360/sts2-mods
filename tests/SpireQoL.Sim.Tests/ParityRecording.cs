namespace SpireQoL.Sim.Tests;

/// <summary>
/// Writing a parity test's recording after a reviewed rule change (decision #402): each scenario's recorded line stays as it was where the test still
/// reads it as the same (Write's same: its own comparison, its reviewed rewrites included), and only the scenarios that changed take the
/// new line. A whole new recording would lose the lines the reviewed rewrites are held to.
/// </summary>
internal static class ParityRecording
{
    /// <param name="same">The test's comparison of a recorded line and the line now (null: equal text).</param>
    public static void Write(string path, IReadOnlyList<string> actual, string recordedPath, Func<string, string, bool>? same = null)
    {
        var recorded = File.Exists(recordedPath) ? File.ReadAllLines(recordedPath) : Array.Empty<string>();
        var lines = actual.Select((line, index) => index < recorded.Length && (same ?? string.Equals)(recorded[index], line) ? recorded[index] : line);
        File.WriteAllLines(path, lines);
    }
}
