namespace Weir.Core.MediaManagers;

/// <summary>The sentences for the ways Deluno's download destinations cannot be read, each saying what to do next.</summary>
internal static class DelunoDestinationNotices
{
    /// <summary>Where a Deluno API key is created and given its permissions.</summary>
    private const string KeysMenu = "System › API Access";

    public static string RouteMissing() =>
        $"Deluno {DelunoDestinationRules.FirstVersionOffering} or later lets Weir check where its download clients save and how its path mappings apply.";

    public static string NeedsImportsScope(string managerLabel) =>
        $"The API key Weir uses for {managerLabel} can't read where downloads go. Give it the Imports permission: " +
        $"in Deluno, open {KeysMenu} and create a key with Media automation access (it includes Imports), then save that key under Setup › Connections › Media managers in Weir.";

    public static string LibraryGone(string managerLabel) =>
        $"{managerLabel} no longer has the library this workflow came from, so Weir can't check where its downloads go. " +
        $"Remove this workflow if the library is gone for good, or unlink it from {managerLabel} to keep it as a Weir-only workflow.";
}
