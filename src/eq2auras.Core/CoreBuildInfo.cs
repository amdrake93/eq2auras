namespace Eq2Auras.Core
{
    /// Reload-probe tracer: the plugin displays this marker on its status label.
    /// If a live reload serves a STALE cached Core, this member won't exist there
    /// and the plugin's guarded read reports it. Bump the letter per reload test.
    public static class CoreBuildInfo
    {
        public static string Marker => "B";
    }
}
