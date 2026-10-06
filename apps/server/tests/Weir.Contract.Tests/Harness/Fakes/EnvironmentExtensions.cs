namespace Weir.Contract.Tests.Harness.Fakes;

public static class EnvironmentExtensions
{
    /// <summary>These settings plus <paramref name="more"/>; a name in both takes the value from <paramref name="more"/>.</summary>
    public static Dictionary<string, string> With(this IReadOnlyDictionary<string, string> settings, params (string Name, string Value)[] more)
    {
        var combined = new Dictionary<string, string>(settings);
        foreach (var (name, value) in more)
        {
            combined[name] = value;
        }

        return combined;
    }

    /// <summary>These settings plus all of <paramref name="more"/>; a name in both takes the value from <paramref name="more"/>.</summary>
    public static Dictionary<string, string> With(this IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> more) =>
        settings.With([.. more.Select(pair => (pair.Key, pair.Value))]);
}
