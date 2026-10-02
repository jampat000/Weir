using Weir.Core.MediaManagers;

namespace Weir.Core.Tests.MediaManagers;

public sealed class QueryStringsTests
{
    [Fact]
    public void Pairs_are_encoded_with_spaces_as_plus_and_other_bytes_as_percent_escapes()
    {
        var encoded = QueryStrings.Encode([new("api_key", "k"), new("query", "Blade Runneré"), new("year", "1982")]);

        Assert.Equal("api_key=k&query=Blade+Runner%C3%A9&year=1982", encoded);
    }

    [Fact]
    public void Unreserved_characters_are_left_as_they_are()
    {
        Assert.Equal("a_b.c-d~e1", QueryStrings.QuotePlus("a_b.c-d~e1"));
    }
}
