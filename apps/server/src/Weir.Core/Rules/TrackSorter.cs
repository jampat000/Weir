namespace Weir.Core.Rules;

/// <summary>
/// One entry in the ordered sorter list. A null
/// <see cref="Value"/> sorts by the field naturally; anything else is a match test.
/// </summary>
public sealed record TrackSorter(string Field, string? Value = null, bool Reversed = false)
{
    /// <summary>A phrase for the selection notes, written the way the operator configured it.</summary>
    public string Describe()
    {
        if (Value is null)
        {
            // The note describes the real ranking (#537 item 1): channels/bitrate rank highest
            // first and commentary ranks last. The golden files recorded the opposite wording;
            // golden/overrides patches the affected cases.
            if (Field is "default" or "forced")
            {
                return $"{Field} {(Reversed ? "last" : "first")}";
            }

            if (Field == "commentary")
            {
                // Commentary is a demotion: it naturally sorts last, not first.
                return $"{Field} {(Reversed ? "first" : "last")}";
            }

            if (Field == "content_tier")
            {
                // Issue #497: the default sorters' leading key — main, then a dub or audio
                // description track, then commentary.
                return Reversed
                    ? "content tier (commentary, then dub/audio description, then main)"
                    : "content tier (main, then dub/audio description, then commentary)";
            }

            if (Field is "codec" or "language" or "title")
            {
                return $"{Field} order";
            }

            // The only fields left (bitrate, channels) are both "larger is better".
            var direction = Reversed ? "lowest first" : "highest first";
            return $"{Field} {direction}";
        }

        var prefix = Reversed ? "not " : string.Empty;
        return $"{prefix}{Field} {Value}";
    }
}
