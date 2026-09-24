namespace Weir.Core.Rules;

/// <summary>The sorter list is not usable.</summary>
public sealed class TrackSorterException : Exception
{
    public TrackSorterException()
    {
    }

    public TrackSorterException(string message)
        : base(message)
    {
    }

    public TrackSorterException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
