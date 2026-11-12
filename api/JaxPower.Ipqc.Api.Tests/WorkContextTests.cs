using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.WorkContextFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class WorkContextTests
{
    [Fact] public async Task R04_05_12_Proof_and_context_id_cannot_authenticate_requests()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var task = await Task(f); var issued = await Issue(f, task, await f.Login());
        foreach (var credential in new[] { issued.ProofSecret, issued.Context.Id.ToString() }) {
            var r = await f.Client(credential).PostAsJsonAsync(Root(task) + "/validate-sources", Sources(f, issued));
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }
    }
    [Theory][InlineData("issue")][InlineData("preflight")][InlineData("read")][InlineData("admin-read")][InlineData("audit")][InlineData("revoke")]
    public async Task R04_02_12_19_Direct_service_rejects_unauthenticated_claims(string operation)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var task = await Task(f); var a = await f.Login();
        var issued = await Issue(f, task, a); var admin = await f.Login("admin");
        var authenticated = Principal(operation is "admin-read" or "audit" or "revoke" ? admin : a,
            operation is "admin-read" or "audit" or "revoke" ? "Admin" : "IPQA");
        var untrusted = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(authenticated.Claims));
        Assert.False(untrusted.Identity!.IsAuthenticated);
        using var scope = f.Factory.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<WorkContextService>();
        var ex = await Assert.ThrowsAsync<PersonalAccessException>(async () => {
            switch (operation) {
                case "issue": await service.IssueAsync(task, Input(f), untrusted, default); break;
                case "preflight": await service.PreflightAsync(task, Sources(f, issued), untrusted, default); break;
                case "read": await service.ReadAsync(issued.Context.Id, task, untrusted, default); break;
                case "admin-read": await service.ReadAsync(issued.Context.Id, null, untrusted, default); break;
                case "audit": await service.AuditAsync(issued.Context.Id, 1, 20, untrusted, default); break;
                default: await service.RevokeAsync(issued.Context.Id, new(1, "Untrusted"), untrusted, default); break;
            }
        });
        Assert.Equal(401, ex.Status);
        Assert.Equal(1, await f.Read(db => db.WorkContextAuditEvents.CountAsync()));
    }

    [Fact] public async Task R04_19_Audit_contains_nonsecret_before_and_after_snapshots()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var task = await Task(f); var a = await f.Login();
        var issued = await Issue(f, task, a); var admin = await f.Login("admin"); var url = $"/api/admin/work-contexts/{issued.Context.Id}";
        (await f.Client(admin.AccessToken).PostAsJsonAsync(url + "/security-revoke", new WorkContextRevokeInput(1, "Synthetic revoke"))).EnsureSuccessStatusCode();
        var body = await f.Client(admin.AccessToken).GetStringAsync(url + "/audit");
        var items = System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("items").EnumerateArray().ToArray();
        var created = items.Single(x => x.GetProperty("action").GetString() == "ContextIssued");
        var revoked = items.Single(x => x.GetProperty("action").GetString() == "ContextSecurityRevoked");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, created.GetProperty("beforeSnapshot").ValueKind);
        Assert.Equal(1, created.GetProperty("afterSnapshot").GetProperty("revision").GetInt64());
        Assert.Equal(1, revoked.GetProperty("beforeSnapshot").GetProperty("revision").GetInt64());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, revoked.GetProperty("beforeSnapshot").GetProperty("securityRevokedAtUtc").ValueKind);
        Assert.Equal(2, revoked.GetProperty("afterSnapshot").GetProperty("revision").GetInt64());
        Assert.Equal("Synthetic revoke", revoked.GetProperty("afterSnapshot").GetProperty("securityRevocationReason").GetString());
        Assert.DoesNotContain(issued.ProofSecret, body);
        Assert.DoesNotContain("proofSecretHash", body, StringComparison.OrdinalIgnoreCase);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task R04_01_06_Receipt_caps_at_original_family_and_both_actual_lines(bool secondLine)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var line = secondLine ? f.Line2 : f.Line1;
        var task = await Task(f, line); var a = await f.Login();
        f.Clock.Now = new DateTimeOffset(a.PersonalSessionExpiresAtUtc!.Value, TimeSpan.Zero).AddHours(-1);
        var refreshed = await f.Refresh(a.RefreshToken!);
        var r = await f.Client(refreshed.AccessToken).PostAsJsonAsync(Root(task), Input(f) with { ExpectedTaskLineId = line });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var c = (await r.Content.ReadFromJsonAsync<WorkContextIssued>())!;
        Assert.Equal(a.PersonalSessionExpiresAtUtc, c.Context.CaptureUntilUtc); Assert.Equal(line, c.Context.LineId);
    }
    [Theory][InlineData("personal_environment_id")][InlineData("personal_session_id")][InlineData("auth_version")]
    public async Task R04_02_12_Forged_authority_claims_fail(string claim)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var task = await Task(f); var a = await f.Login();
        var token = f.Token(a, claims => claims.Where(c => c.Type != claim).Append(new System.Security.Claims.Claim(claim, claim == "auth_version" ? "9999" : Guid.NewGuid().ToString())).ToList());
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client(token).PostAsJsonAsync(Root(task), Input(f))).StatusCode);
    }
    [Fact] public async Task R04_01_05_06_07_08_15_19_20_21_Issuance_preflight_and_metadata_have_no_business_effects()
    { await using var f = await PersonalSessionFixture.CreateAsync(); await Foundation(f); }
    internal static async Task Foundation(PersonalSessionFixture f)
    {
        var task = await Task(f); var a = await f.Login(); var b = await f.Login("dev-ipqa-2");
        var issued = await Issue(f, task, a); var second = await Issue(f, task, a);
        Assert.NotEqual(issued.Context.Id, second.Context.Id); Assert.Equal(43, issued.ProofSecret.Length);
        Assert.False(issued.CanExecute); Assert.Equal(TestAccounts.Ipqa1Id, issued.Context.UserId);
        Assert.Equal(a.PersonalSessionId, issued.Context.IssuedPersonalSessionId);
        Assert.Equal(f.Clock.Now.UtcDateTime.AddHours(72), issued.Context.CaptureUntilUtc);
        Assert.Equal(64, await f.Read(db => db.WorkContexts.Where(x => x.Id == issued.Context.Id).Select(x => x.ProofSecretHash.Length).SingleAsync()));
        var metadata = await f.Client(b.AccessToken).GetAsync(Root(task) + "/" + issued.Context.Id);
        var json = await metadata.Content.ReadAsStringAsync(); Assert.DoesNotContain("proof", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("no-store", metadata.Headers.CacheControl!.ToString());
        var preflight = await f.Client(b.AccessToken).PostAsJsonAsync(Root(task) + "/validate-sources", Sources(f, issued));
        Assert.Equal(HttpStatusCode.OK, preflight.StatusCode);
        Assert.False((await preflight.Content.ReadFromJsonAsync<WorkContextPreflight>())!.AcceptsWork);
        Assert.Equal(2, await f.Read(db => db.WorkContextAuditEvents.CountAsync()));
        var detail = await f.Client(b.AccessToken).GetStringAsync($"/api/line-patrol/tasks/{task}"); Assert.Contains("\"canExecute\":false", detail);
        Assert.Equal(0, await f.Read(db => db.PatrolTaskSubmissions.CountAsync()));
    }
    [Theory][InlineData("proof")][InlineData("owner")][InlineData("task")][InlineData("line")][InlineData("workflow")][InlineData("environment")][InlineData("duplicate")][InlineData("empty")]
    public async Task R04_03_13_14_15_16_Provenance_rejects_malformed_and_cross_scope_sources(string kind)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var task = await Task(f); var a = await f.Login(); var issued = await Issue(f, task, a);
        var input = Sources(f, issued);
        input = kind switch {
            "proof" => input with { Sources = [input.Sources[0] with { ProofSecret = "bad" }] },
            "owner" => input with { Sources = [input.Sources[0] with { ContributorUserIdAssertion = TestAccounts.Ipqa2Id }] },
            "task" => input with { Sources = [input.Sources[0] with { WorkContextId = Guid.NewGuid() }] },
            "line" => input with { ExpectedTaskLineId = f.Line2 }, "workflow" => input with { ExpectedWorkflowVersion = 1 },
            "environment" => input with { ExpectedEnvironmentId = "other" }, "duplicate" => input with { Sources = [input.Sources[0], input.Sources[0]] },
            _ => input with { Sources = [] } };
        var r = await f.Client(a.AccessToken).PostAsJsonAsync(Root(task) + "/validate-sources", input);
        Assert.Equal(kind is "duplicate" or "empty" ? HttpStatusCode.BadRequest : HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(1, await f.Read(db => db.WorkContextAuditEvents.CountAsync()));
    }
    [Theory][InlineData("anonymous")][InlineData("PQE")][InlineData("Admin")][InlineData("family")][InlineData("logout")]
    public async Task R04_02_12_17_Http_and_direct_service_require_current_person_authority(string kind)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var task = await Task(f);
        var a = await f.Login(kind is "PQE" ? "pqe" : kind is "Admin" ? "admin" : "dev-ipqa-1");
        if (kind == "logout") await f.Client(a.AccessToken).PostAsJsonAsync("/api/auth/logout", new RefreshInput(a.RefreshToken));
        var token = kind == "family" ? f.Token(a, claims => claims.Where(c => c.Type != "personal_session_id").ToList()) : a.AccessToken;
        var client = kind == "anonymous" ? f.Anonymous : f.Client(token);
        var r = await client.PostAsync(Root(task), new StringContent("broken", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(kind is "PQE" or "Admin" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal("no-store", r.Headers.CacheControl!.ToString());
        using var scope = f.Factory.Services.CreateScope();
        var principal = kind == "anonymous" ? new System.Security.Claims.ClaimsPrincipal() : Principal(a, kind is "PQE" or "Admin" ? kind : "IPQA");
        if (kind == "family") principal = Principal(a with { AccessToken = token });
        await Assert.ThrowsAsync<PersonalAccessException>(() => scope.ServiceProvider.GetRequiredService<WorkContextService>().IssueAsync(task, Input(f), principal, default));
    }
    [Fact] public async Task R04_04_Historical_task_is_not_implicitly_admitted()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var task = await f.Task(); var auth = await f.Login();
        Assert.Equal(HttpStatusCode.Conflict, (await f.Client(auth.AccessToken).PostAsJsonAsync(Root(task), Input(f))).StatusCode);
    }
    [Fact] public async Task R04_16_17_Unknown_fields_limits_and_authorization_order()
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var task = await Task(f); var a = await f.Login(); var client = f.Client(a.AccessToken);
        var bad = await client.PostAsJsonAsync(Root(task), new { expectedTaskLineId = f.Line1, expectedWorkflowVersion = 2, expectedEnvironmentId = PersonalSessionFixture.Environment, userId = TestAccounts.Ipqa2Id });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        for (var i = 0; i < 9; i++) await Issue(f, task, a);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync(Root(task), Input(f))).StatusCode);
        await client.PostAsJsonAsync("/api/auth/logout", new RefreshInput(a.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root(task), Input(f))).StatusCode);
    }

    [Theory][InlineData("null")][InlineData("mixed")][InlineData("large")][InlineData("body")]
    public async Task R04_15_16_Bad_source_batches_do_not_partially_validate_or_write(string kind)
    {
        await using var f = await PersonalSessionFixture.CreateAsync(); var task = await Task(f); var a = await f.Login(); var issued = await Issue(f, task, a);
        var input = Sources(f, issued);
        input = kind switch { "null" => input with { Sources = [null!] },
            "mixed" => input with { Sources = [input.Sources[0], new(Guid.NewGuid(), issued.ProofSecret, a.User.Id)] },
            "large" => input with { Sources = Enumerable.Range(0, 101).Select(_ => new WorkContextSource(Guid.NewGuid(), issued.ProofSecret, TestAccounts.Ipqa1Id)).ToList() }, _ => input };
        var r = kind == "body" ? await f.Client(a.AccessToken).PostAsync(Root(task) + "/validate-sources", new StringContent(new string(' ', 32769), System.Text.Encoding.UTF8, "application/json")) :
            await f.Client(a.AccessToken).PostAsJsonAsync(Root(task) + "/validate-sources", input);
        Assert.Equal(kind == "mixed" ? HttpStatusCode.Conflict : kind == "body" ? HttpStatusCode.RequestEntityTooLarge : HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(1, await f.Read(db => db.WorkContextAuditEvents.CountAsync()));
    }
}
