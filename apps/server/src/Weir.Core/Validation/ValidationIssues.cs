using System.Globalization;
using Weir.Core.Json;

namespace Weir.Core.Validation;

/// <summary>One validation error in a 422 body: <c>type</c>, <c>loc</c>, <c>msg</c>, <c>input</c> and optional <c>ctx</c>.</summary>
public sealed record ValidationIssue(string Type, IReadOnlyList<object> Loc, string Msg, PyJson Input, PyDict? Ctx = null)
{
    public PyDict ToPyDict()
    {
        var dict = new PyDict()
            .Set("type", Type)
            .Set("loc", new PyList(Loc.Select(part => part is int i ? PyJson.Of(i) : PyJson.Of(Convert.ToString(part, CultureInfo.InvariantCulture)))))
            .Set("msg", Msg)
            .Set("input", Input);
        if (Ctx is not null)
        {
            dict.Set("ctx", Ctx);
        }

        return dict;
    }
}

/// <summary>An invalid request: answered with 422 and <c>{"detail": [...]}</c>.</summary>
public sealed class RequestValidationException : Exception
{
    public RequestValidationException()
    {
        Issues = [];
    }

    public RequestValidationException(string message)
        : base(message)
    {
        Issues = [];
    }

    public RequestValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Issues = [];
    }

    public RequestValidationException(IReadOnlyList<ValidationIssue> issues)
        : base("Request validation failed.")
    {
        Issues = issues;
    }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public PyDict ToBody() => new PyDict().Set("detail", new PyList(Issues.Select(issue => (PyJson)issue.ToPyDict())));
}

/// <summary>Collects errors across path, query, header and body parameters, in that order.</summary>
public sealed class ValidationIssues
{
    private readonly List<ValidationIssue> _issues = [];

    public IReadOnlyList<ValidationIssue> All => _issues;

    public bool Any => _issues.Count > 0;

    public void Add(ValidationIssue issue) => _issues.Add(issue);

    public void ThrowIfAny()
    {
        if (_issues.Count > 0)
        {
            throw new RequestValidationException([.. _issues]);
        }
    }
}
