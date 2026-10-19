using System.Runtime.CompilerServices;

namespace Morphdrop.Tests;

internal static class TestSetup
{
    /// <summary>Runs before any test: a private data folder and no logging, so the user's real ones are never touched.</summary>
    [ModuleInitializer]
    internal static void Isolate()
    {
        Environment.SetEnvironmentVariable(AppPaths.OverrideVariable, Path.Combine(Path.GetTempPath(), "morphdrop-test-data"));
        Log.Enabled = false;
    }
}
