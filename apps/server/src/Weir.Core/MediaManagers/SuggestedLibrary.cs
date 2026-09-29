namespace Weir.Core.MediaManagers;

/// <summary>
/// A library Weir can offer to create because a connected manager or download client already knows its folders.
/// Never saved by itself: the person confirms and edits it first.
/// </summary>
/// <param name="LibraryId">The existing library with no folders yet that this would fill in, or null when it would be a new one.</param>
/// <param name="Name">What the library is called.</param>
/// <param name="MediaType">Movies or TV, as <see cref="MediaManagerKinds.Movie"/> or <see cref="MediaManagerKinds.Tv"/>.</param>
/// <param name="WatchedFolder">Where finished downloads land.</param>
/// <param name="OutputFolder">Where cleaned files go; empty when there is no sensible default.</param>
/// <param name="SourceLabel">Who reported the watched folder, e.g. "Deluno" or "SABnzbd".</param>
/// <param name="ManagerConnectionIds">The media managers that cover this media type, to link the library to.</param>
public sealed record SuggestedLibrary(
    long? LibraryId,
    string Name,
    string MediaType,
    string WatchedFolder,
    string OutputFolder,
    string SourceLabel,
    IReadOnlyList<long> ManagerConnectionIds);

/// <summary>The libraries to offer, and plain sentences about anything connected that could not contribute a folder.</summary>
public sealed record LibrarySuggestionSet(IReadOnlyList<SuggestedLibrary> Libraries, IReadOnlyList<string> Notes);
