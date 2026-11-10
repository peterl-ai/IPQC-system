using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class OfflineAccessReceiptTests
{
    [Theory]
    [InlineData(14)][InlineData(1)]
    public async Task Receipt_uses_server_utc_72_hour_cap_and_immutable_family_expiry(int remainingDays)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var auth = await f.Login();
        var expiry = f.Clock.Now.UtcDateTime.AddDays(remainingDays);
        await f.Edit(async db => { (await db.RefreshTokenSessions.SingleAsync()).ExpiresAtUtc = expiry; });
        var client = f.Client(auth.AccessToken);
        var response = await client.PostAsync("/api/line-patrol/access/verify", null); response.EnsureSuccessStatusCode();
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var receipt = (await response.Content.ReadFromJsonAsync<OfflineAccessReceipt>())!;
        Assert.Equal(1, receipt.Version); Assert.Equal(TestAccounts.Ipqa1Id, receipt.UserId);
        Assert.Equal(auth.PersonalSessionId, receipt.PersonalSessionId); Assert.Equal(PersonalSessionFixture.Environment, receipt.EnvironmentId);
        Assert.Equal(f.Clock.Now.UtcDateTime, receipt.VerifiedAtUtc);
        Assert.Equal(f.Clock.Now.UtcDateTime.AddDays(Math.Min(3, remainingDays)), receipt.OfflineAccessUntilUtc);
        Assert.Equal(expiry, receipt.PersonalSessionExpiresAtUtc); Assert.Equal(DateTimeKind.Utc, receipt.VerifiedAtUtc.Kind);
        Assert.DoesNotContain("refreshToken", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client(await response.Content.ReadAsStringAsync()).GetAsync("/api/line-patrol/lines")).StatusCode);
        f.Clock.Now = f.Clock.Now.AddMinutes(1);
        var refreshed = await f.Refresh(auth.RefreshToken!);
        Assert.Equal(expiry, refreshed.PersonalSessionExpiresAtUtc);
        var next = (await f.Client(refreshed.AccessToken).PostAsync("/api/line-patrol/access/verify", null)); next.EnsureSuccessStatusCode();
        var renewed = (await next.Content.ReadFromJsonAsync<OfflineAccessReceipt>())!;
        Assert.Equal(f.Clock.Now.UtcDateTime, renewed.VerifiedAtUtc); Assert.Equal(expiry, renewed.PersonalSessionExpiresAtUtc);
        Assert.Equal(2, await f.Read(db => db.RefreshTokenSessions.CountAsync()));
    }

    [Fact]
    public async Task Family_expiry_is_strict_even_while_access_token_remains_valid()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var auth = await f.Login();
        await f.Edit(async db => { (await db.RefreshTokenSessions.SingleAsync()).ExpiresAtUtc = f.Clock.Now.UtcDateTime; });
        Assert.Equal(HttpStatusCode.OK, (await f.Client(auth.AccessToken).GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client(auth.AccessToken).PostAsync("/api/line-patrol/access/verify", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshInput(auth.RefreshToken))).StatusCode);
    }
}
