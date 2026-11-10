using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class PersonalSessionTests
{
    [Fact]
    public async Task Login_families_rotate_rows_but_never_identity_environment_or_absolute_expiry()
    {
        await using var f = await PersonalSessionFixture.CreateAsync();
        var first = await f.Login(); var second = await f.Refresh(first.RefreshToken!); var third = await f.Refresh(second.RefreshToken!);
        Assert.NotNull(first.PersonalSessionId); Assert.Equal(first.PersonalSessionId, third.PersonalSessionId);
        Assert.Equal(first.PersonalSessionEnvironmentId, third.PersonalSessionEnvironmentId);
        Assert.Equal(first.PersonalSessionExpiresAtUtc, third.PersonalSessionExpiresAtUtc);
        Assert.NotEqual(first.PersonalSessionId, (await f.Login()).PersonalSessionId);
        var rows = await f.Read(db => db.RefreshTokenSessions.Where(x => x.PersonalSessionId == first.PersonalSessionId).ToListAsync());
        Assert.Equal(3, rows.Count); Assert.Equal(3, rows.Select(x => x.Id).Distinct().Count());
        Assert.Single(rows, x => x.RevokedAtUtc == null); Assert.Equal(2, rows.Count(x => x.ReplacedBySessionId != null));
        Assert.All(rows, x => { Assert.Equal(64, x.TokenHash.Length); Assert.NotEqual(third.RefreshToken, x.TokenHash); });
        Assert.Equal(HttpStatusCode.OK, (await f.Client(first.AccessToken).GetAsync("/api/line-patrol/lines")).StatusCode);
    }

    [Theory]
    [InlineData("missing")][InlineData("empty")][InlineData("malformed")][InlineData("duplicate")]
    [InlineData("owner")][InlineData("environment")][InlineData("environment-duplicate")][InlineData("unknown")]
    public async Task Signed_claims_cannot_substitute_for_current_database_authority(string kind)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var auth = await f.Login();
        var other = await f.Login("dev-ipqa-2");
        var token = f.Token(auth, claims =>
        {
            if (kind == "duplicate") { claims.Add(new(PersonalAccess.FamilyClaim, auth.PersonalSessionId.ToString()!)); return claims; }
            if (kind == "environment-duplicate") { claims.Add(new(PersonalAccess.EnvironmentClaim, PersonalSessionFixture.Environment)); return claims; }
            var type = kind == "environment" ? PersonalAccess.EnvironmentClaim : PersonalAccess.FamilyClaim;
            claims.RemoveAll(x => x.Type == type);
            if (kind != "missing") claims.Add(new Claim(type, kind switch { "empty" => Guid.Empty.ToString(),
                "malformed" => "not-a-guid", "owner" => other.PersonalSessionId.ToString()!, "environment" => "other-environment", _ => Guid.NewGuid().ToString() }));
            return claims;
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client(token).GetAsync("/api/line-patrol/lines")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.Client(auth.AccessToken).GetAsync("/api/line-patrol/lines")).StatusCode);
    }

    [Fact]
    public async Task Old_jwt_and_null_family_refresh_remain_legacy_only_without_silent_upgrade()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var auth = await f.Login();
        var legacyJwt = f.Token(auth, c => c.Where(x => x.Type is not (PersonalAccess.FamilyClaim or PersonalAccess.EnvironmentClaim)).ToList());
        await f.Edit(async db => { var row = await db.RefreshTokenSessions.SingleAsync(); row.PersonalSessionId = null; row.PersonalSessionEnvironmentId = null; });
        Assert.Equal(HttpStatusCode.OK, (await f.Client(legacyJwt).GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.Client(legacyJwt).GetAsync("/api/completed-patrol-records")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client(legacyJwt).GetAsync("/api/line-patrol/lines")).StatusCode);
        var next = await f.Refresh(auth.RefreshToken!); Assert.Null(next.PersonalSessionId); Assert.Null(next.PersonalSessionEnvironmentId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client(next.AccessToken).GetAsync("/api/line-patrol/lines")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await f.Client(legacyJwt).PostAsJsonAsync("/api/auth/logout", new RefreshInput(next.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshInput(next.RefreshToken))).StatusCode);
    }

    [Theory]
    [InlineData("ancestor")][InlineData("missing")][InlineData("foreign")]
    public async Task Logout_closes_signed_family_without_redirecting_to_other_tokens(string kind)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var a = await f.Login(); var next = await f.Refresh(a.RefreshToken!);
        var independent = await f.Login(); var b = await f.Login("dev-ipqa-2");
        var supplied = kind == "ancestor" ? a.RefreshToken : kind == "foreign" ? b.RefreshToken : null;
        Assert.Equal(HttpStatusCode.NoContent, (await f.Client(a.AccessToken).PostAsJsonAsync("/api/auth/logout", new RefreshInput(supplied))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client(next.AccessToken).GetAsync("/api/line-patrol/lines")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.Client(next.AccessToken).GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.Client(independent.AccessToken).GetAsync("/api/line-patrol/lines")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.Client(b.AccessToken).GetAsync("/api/line-patrol/lines")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshInput(next.RefreshToken))).StatusCode);
    }

    [Fact]
    public async Task Prior_session_reconciliation_is_owner_only_idempotent_and_allowed_during_forced_change()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var old = await f.Login(); var current = await f.Login(); var b = await f.Login("dev-ipqa-2");
        var route = $"/api/auth/personal-sessions/{old.PersonalSessionId}/revoke";
        Assert.Equal(HttpStatusCode.NoContent, (await f.Client(b.AccessToken).PostAsync(route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.Client(old.AccessToken).GetAsync("/api/line-patrol/lines")).StatusCode);
        await f.Edit(async db => { (await db.ApplicationUsers.SingleAsync(x => x.Id == TestAccounts.Ipqa1Id)).MustChangePassword = true; });
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.NoContent, (await f.Client(current.AccessToken).PostAsync(route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshInput(old.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Client(current.AccessToken).GetAsync("/api/line-patrol/lines")).StatusCode);
    }

    [Fact]
    public async Task Missing_configuration_preserves_legacy_and_environment_less_family_cannot_promote_on_refresh()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(environment: null); var auth = await f.Login();
        Assert.NotNull(auth.PersonalSessionId); Assert.Null(auth.PersonalSessionEnvironmentId);
        Assert.Equal(HttpStatusCode.OK, (await f.Client(auth.AccessToken).GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await f.Client(auth.AccessToken).GetAsync("/api/line-patrol/lines")).StatusCode);
        Assert.Null((await f.Refresh(auth.RefreshToken!)).PersonalSessionEnvironmentId);
    }
}
