using System.Text;
using Weir.Contract.Tests.Harness.Fakes;

namespace Weir.Contract.Tests.MediaManagers;

/// <summary>A real HTTP server that answers every request 2xx with a body that is not JSON, such as a reverse proxy's sign-in page.</summary>
internal sealed class ManagerHtmlServer : FakeHttpServer
{
    private static readonly byte[] SignInPage = Encoding.UTF8.GetBytes("<html><body>Please sign in to continue</body></html>");

    protected override HttpAnswer Answer(RecordedRequest request) => new(200, SignInPage, "text/html");
}
