namespace Weir.Tray;

/// <summary>The runtime home exists and another user account owns it, so Weir will not start there.</summary>
sealed class RuntimeHomeOwnedByAnotherAccountException(string runtimeHome, string owner)
    : Exception(
        $"Weir's data folder {runtimeHome} belongs to another Windows account ({owner}), so Weir will not use it: it may hold "
        + "that person's data, or have been created by someone else to read yours. If the folder is yours, take "
        + "ownership of it (folder Properties > Security > Advanced) or move it aside, then start Weir again. To keep "
        + "Weir's data somewhere else, set WEIR_HOME.")
{
    public string RuntimeHome { get; } = runtimeHome;
}
