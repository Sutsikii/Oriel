namespace Oriel;

/// <summary>
/// The diagnostic ids of experimental APIs: public, but expected to change
/// before 1.0. Using one is a warning apps silence by its id, so that they
/// know which change to watch. See docs/diagnostics.md.
/// </summary>
internal static class Experiments
{
    public const string Url = "https://github.com/Sutsikii/Oriel/blob/main/docs/diagnostics.md#experimental-apis";

    /// <summary><see cref="OrielApp.CallFailed"/>, which logging through <c>ILogger</c> may replace.</summary>
    public const string CallFailed = "ORIEL901";

    /// <summary>Reading and writing clients with <see cref="TypeScriptClient"/>, which will cover several assemblies.</summary>
    public const string TypeScriptClients = "ORIEL902";
}
