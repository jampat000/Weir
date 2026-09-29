using Weir.Core.MediaManagers;

namespace Weir.Core.Tests.MediaManagers;

/// <summary><see cref="QBittorrentLoginRules"/>: the 4.x and 5.x answers to <c>POST /api/v2/auth/login</c> (#842).</summary>
public sealed class QBittorrentLoginRulesTests
{
    [Fact]
    public void A_4x_login_with_an_Ok_body_is_accepted_and_keeps_only_the_cookie_pair()
    {
        var login = QBittorrentLoginRules.Read(200, "Ok.", "SID=abc123; HttpOnly; path=/");

        Assert.True(login.Accepted);
        Assert.Equal("SID=abc123", login.SessionCookie);
    }

    [Fact]
    public void A_4x_login_with_an_Ok_body_and_no_cookie_is_accepted_without_a_session()
    {
        var login = QBittorrentLoginRules.Read(200, "Ok.", null);

        Assert.True(login.Accepted);
        Assert.Null(login.SessionCookie);
    }

    [Fact]
    public void A_5x_login_with_204_an_empty_body_and_a_QBT_SID_cookie_is_accepted()
    {
        var login = QBittorrentLoginRules.Read(204, string.Empty, "QBT_SID_8081=xyz789; HttpOnly; SameSite=Strict; path=/");

        Assert.True(login.Accepted);
        Assert.Equal("QBT_SID_8081=xyz789", login.SessionCookie);
    }

    [Theory]
    [InlineData(204, "", null)]
    [InlineData(204, "", "")]
    [InlineData(204, "", "SID=; path=/")]
    [InlineData(200, "Fails.", "SID=abc123")]
    [InlineData(200, "<html>a login page</html>", "SID=abc123")]
    [InlineData(401, "", "SID=abc123")]
    [InlineData(403, "Ok.", "SID=abc123")]
    public void A_login_that_is_not_a_success_answer_is_refused(int status, string body, string? setCookie)
    {
        var login = QBittorrentLoginRules.Read(status, body, setCookie);

        Assert.False(login.Accepted);
        Assert.Null(login.SessionCookie);
    }
}
