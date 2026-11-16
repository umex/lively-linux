namespace lively.Core.Wallpapers
{
    /// <summary>Executable and arguments of a player process.</summary>
    public sealed record PlayerLaunchInfo(string FileName, IReadOnlyList<string> Arguments)
    {
        public override string ToString() => FileName + " " + string.Join(' ', Arguments.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a));
    }
}
