using Weir.Core.Json;

namespace Weir.Core.Tests.Json;

/// <summary>Limits that keep a hostile document from ending the process or tying up a thread.</summary>
public sealed class JsonParserLimitsTests
{
    [Fact]
    public void Nesting_at_the_limit_parses()
    {
        var depth = PyJsonParser.MaxDepth;
        var parsed = PyJsonParser.Parse(new string('[', depth) + new string(']', depth));

        Assert.IsType<PyList>(parsed);
    }

    [Theory]
    [InlineData('[', ']')]
    [InlineData('{', '}')]
    public void Nesting_past_the_limit_is_a_decode_error_not_a_stack_overflow(char open, char close)
    {
        // Far deeper than the limit, and deep enough to overflow the stack if nothing stopped it.
        const int Depth = 100_000;
        var text = open == '['
            ? new string('[', Depth) + new string(']', Depth)
            : string.Concat(Enumerable.Repeat("{\"a\":", Depth)) + "1" + new string(close, Depth);

        var error = Assert.Throws<PyJsonDecodeException>(() => PyJsonParser.Parse(text));

        Assert.Equal($"Nested more than {PyJsonParser.MaxDepth} levels deep", error.Detail);
    }

    [Fact]
    public void An_integer_with_too_many_digits_is_a_decode_error()
    {
        var error = Assert.Throws<PyJsonDecodeException>(() => PyJsonParser.Parse(new string('9', PyJsonParser.MaxIntegerDigits + 1)));

        Assert.Equal($"Integer longer than {PyJsonParser.MaxIntegerDigits} digits", error.Detail);
    }

    [Fact]
    public void An_integer_at_the_digit_limit_parses_including_its_sign()
    {
        Assert.IsType<PyInt>(PyJsonParser.Parse("-" + new string('9', PyJsonParser.MaxIntegerDigits)));
    }
}
