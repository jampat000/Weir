namespace Weir.Core.Paging;

/// <summary>What the values of one part of a sort key are, and so how two of them compare.</summary>
public enum KeysetValueKind
{
    /// <summary>A whole number, compared by size.</summary>
    Number,

    /// <summary>Text, compared by its UTF-8 bytes: the order SQLite's default collation gives.</summary>
    Text,

    /// <summary>Text, compared by its UTF-8 bytes with the letters A to Z read as a to z: SQLite's <c>NOCASE</c> collation.</summary>
    TextIgnoringCase,
}

/// <summary>One part of a sort key: what its values are and which way it runs. A part with no value sorts before every value.</summary>
public readonly record struct KeysetPart(KeysetValueKind Kind, SortDirection Direction);
