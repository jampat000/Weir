using Microsoft.AspNetCore.StaticFiles;

namespace Weir.Api.Web;

/// <summary>
/// Content types for web app files: .NET's registered types, except that <c>.js</c> is
/// <c>application/javascript</c> and every <c>text/*</c> type gets <c>; charset=utf-8</c>, the headers
/// existing clients and the contract suite expect.
/// </summary>
public static class WebContentTypes
{
    public static FileExtensionContentTypeProvider CreateProvider()
    {
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[".js"] = "application/javascript";
        return provider;
    }

    /// <summary>The type for <paramref name="fileName"/>, with a charset on text types.</summary>
    public static string For(FileExtensionContentTypeProvider provider, string fileName, string fallback)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return WithCharset(provider.TryGetContentType(fileName, out var contentType) ? contentType : fallback);
    }

    public static string WithCharset(string contentType)
    {
        ArgumentNullException.ThrowIfNull(contentType);
        return contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) &&
            !contentType.Contains("charset=", StringComparison.OrdinalIgnoreCase)
            ? contentType + "; charset=utf-8"
            : contentType;
    }
}
