using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.NgPicSelectionFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class NgPicSelectionSecurityTests
{
    [Fact] public async Task Personal_authority_is_distinct_from_provenance_for_every_entry() { await using var f = await PersonalSessionFixture.CreateAsync(); await Authority(f); }
    internal static async Task Authority(PersonalSessionFixture f) {
        var u = await Setup(f); var s = u.S; var b = Input(u);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous.PostAsync(Url(s), new StringContent("{broken", Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous.GetAsync(ReadUrl(s))).StatusCode);
        foreach (var name in new[] { "admin", "pqe" }) {
            var actor = await f.Login(name);
            Assert.Equal(HttpStatusCode.Forbidden, (await s.Client(actor).PostAsJsonAsync(Url(s), b)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await s.Client(actor).GetAsync(ReadUrl(s))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await s.Client(actor).GetAsync(ReceiptUrl(s, b.Operation.OperationId))).StatusCode);
        }
        foreach (var token in new[] { f.Token(s.B, cs => cs.Where(c => c.Type != PersonalAccess.FamilyClaim).ToList()), s.Source.ProofSecret })
            Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client(token).PostAsJsonAsync(Url(s), b)).StatusCode);
        await using (var scope = f.Factory.Services.CreateAsyncScope()) {
            var service = scope.ServiceProvider.GetRequiredService<SharedDraftService>();
            var fake = new ClaimsPrincipal(new ClaimsIdentity(WorkContextFixture.Principal(s.B).Claims));
            Assert.Equal(401, (await Assert.ThrowsAsync<PersonalAccessException>(() => service.AcceptNgPicsAsync(s.TaskId, null!, fake, default))).Status);
            Assert.Equal(401, (await Assert.ThrowsAsync<PersonalAccessException>(() => service.ReadNgPicsAsync(s.TaskId, fake, default))).Status);
            Assert.Equal(401, (await Assert.ThrowsAsync<PersonalAccessException>(() => service.NgPicReceiptAsync(s.TaskId, b.Operation.OperationId, fake, default))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<PersonalAccessException>(() => service.AcceptNgPicsAsync(s.TaskId, b, WorkContextFixture.Principal(u.Admin, "IPQA"), default))).Status);
        }
        await f.Edit(async db => (await db.ApplicationUsers.SingleAsync(x => x.Id == s.B.User.Id)).MustChangePassword = true);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client().PostAsJsonAsync(Url(s), b)).StatusCode);
        await f.Edit(async db => (await db.ApplicationUsers.SingleAsync(x => x.Id == s.B.User.Id)).MustChangePassword = false);
        (await s.Client().PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.B.RefreshToken))).EnsureSuccessStatusCode();
        var before = await Facts(f);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client().PostAsJsonAsync(Url(s), b)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client().GetAsync(ReadUrl(s))).StatusCode);
        Assert.Equal(before, await Facts(f));
    }
    [Fact] public async Task Missing_sets_unknown_snapshot_fields_duplicates_and_coercions_are_rejected() { await using var f = await PersonalSessionFixture.CreateAsync(); await Shape(f); }
    internal static async Task Shape(PersonalSessionFixture f) {
        var u = await Setup(f); var b = Input(u); var text = JsonSerializer.Serialize(b, DraftCanonical.Json); var before = await Facts(f);
        var missing = JsonNode.Parse(text)!; missing["operation"]!.AsObject().Remove("picIds");
        var names = JsonNode.Parse(text)!; names["operation"]!["name"] = "Client invented PIC name";
        var dept = JsonNode.Parse(text)!; dept["operation"]!["department"] = "Client invented department";
        var active = JsonNode.Parse(text)!; active["operation"]!["isActive"] = true;
        foreach (var bad in new[] { missing.ToJsonString(), names.ToJsonString(), dept.ToJsonString(), active.ToJsonString(),
            text.Replace("\"picIds\":[", "\"PICIDS\":[],\"picIds\":["), text.Replace("\"baseDraftVersion\":1", "\"baseDraftVersion\":\"1\"") }) {
            Assert.NotEqual(text, bad); Assert.Equal(HttpStatusCode.BadRequest, (await u.S.Client().PostAsync(Url(u.S), new StringContent(bad, Encoding.UTF8, "application/json"))).StatusCode);
        }
        Assert.Equal(before, await Facts(f));
    }
    [Theory][InlineData("logout")][InlineData("self-password")][InlineData("expiry")][InlineData("refresh")]
    public async Task Normal_A_session_changes_do_not_security_revoke_queued_PIC_work(string action) { await using var f = await PersonalSessionFixture.CreateAsync(); await Normal(f, action); }
    internal static async Task Normal(PersonalSessionFixture f, string action) {
        var u = await Setup(f); var s = u.S; var first = (await Accept(u, Input(u))).Operation; var queued = Input(u, 2, [], first);
        if (action == "logout") (await s.Client(s.A).PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.A.RefreshToken))).EnsureSuccessStatusCode();
        if (action == "self-password") (await s.Client(s.A).PostAsJsonAsync("/api/auth/change-password", new ChangePasswordInput(TestAccounts.Password, "SyntheticChanged123A"))).EnsureSuccessStatusCode();
        if (action == "refresh") _ = await f.Refresh(s.A.RefreshToken!);
        if (action == "expiry") { f.Clock.Now = f.Clock.Now.AddDays(15); u = u with { S = s with { B = await f.Login("dev-ipqa-2") } }; }
        var accepted = (await Accept(u, queued)).Operation; Assert.Equal(s.A.User.Id, accepted.ContributorUserId); Assert.Equal(u.S.B.PersonalSessionId, accepted.ActorPersonalSessionId);
        Assert.Null(await f.Read(db => db.WorkContexts.Where(x => x.Id == s.Source.Context.Id).Select(x => x.SecurityRevokedAtUtc).SingleAsync()));
        EqualReceipt(first, (await u.S.Client().GetFromJsonAsync<NgPicSelectionReceipt>(ReceiptUrl(s, first.OperationId)))!);
    }
    [Theory][InlineData("reset")][InlineData("role")][InlineData("deactivate")][InlineData("explicit")]
    public async Task Sticky_A_revocation_quarantines_fresh_work_but_not_original_receipts_or_accepted_NG(string action) { await using var f = await PersonalSessionFixture.CreateAsync(); await Security(f, action); }
    internal static async Task Security(PersonalSessionFixture f, string action) {
        var u = await Setup(f); var s = u.S; var original = Input(u); var first = (await Accept(u, original)).Operation;
        await SharedDraftFixture.Revoke(s, action);
        if (action is "role" or "deactivate") {
            var admin = await f.Login("admin"); var legacy = f.Client(f.Token(admin, cs => cs.Where(c => c.Type != PersonalAccess.FamilyClaim).ToList()));
            var restore = action == "role" ? await legacy.PutAsJsonAsync($"/api/users/{s.A.User.Id}", new UserUpdateInput("Synthetic inspector", "IPQA")) : await legacy.PostAsync($"/api/users/{s.A.User.Id}/activate", null);
            restore.EnsureSuccessStatusCode();
        }
        var before = await Facts(f); var queued = Input(u, 2, [], first); var bad = await s.Client().PostAsJsonAsync(Url(s), queued);
        Assert.Equal(HttpStatusCode.Conflict, bad.StatusCode); var detail = JsonDocument.Parse(await bad.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("SourceSecurityRevoked", detail.GetProperty("code").GetString()); Assert.True(detail.GetProperty("quarantineRequired").GetBoolean());
        Assert.Equal(queued.Operation.OperationId, detail.GetProperty("operationIds")[0].GetGuid());
        EqualReceipt(first, (await Accept(u, original with { SourceProof = null })).Operation); Assert.Equal(before, await Facts(f));
        var next = (await Accept(u, Input(u, 2, [], first, u.SourceB))).Operation;
        Assert.Equal(s.B.User.Id, next.ContributorUserId); Assert.Equal(s.A.User.Id, first.ContributorUserId);
        Assert.Equal(u.Sample!.OperationId, Assert.Single(next.NgObservation.References).OperationId);
        EqualReceipt(first, (await s.Client().GetFromJsonAsync<NgPicSelectionReceipt>(ReceiptUrl(s, first.OperationId)))!);
    }
    [Theory][InlineData("snapshot")][InlineData("attempt")]
    public async Task Scope_becoming_unavailable_keeps_history_without_current_assignment(string kind) { await using var f = await PersonalSessionFixture.CreateAsync(); await Unavailable(f, kind); }
    internal static async Task Unavailable(PersonalSessionFixture f, string kind) {
        var u = await Setup(f); var b = Input(u); var original = (await Accept(u, b)).Operation;
        // Technical applicability fault; this does not fabricate a business reinspection producer.
        if (kind == "attempt") await f.Edit(db => db.InspectionAttemptApplicabilities.Where(x => x.PatrolTaskItemId == u.S.ItemId).ExecuteDeleteAsync());
        else await f.Edit(async db => (await db.PatrolTasks.SingleAsync(x => x.Id == u.S.TaskId)).PlanningSnapshotJson = "{invalid");
        var before = await Facts(f); Assert.Equal(HttpStatusCode.Conflict, (await u.S.Client().PostAsJsonAsync(Url(u.S), Input(u, 2, [], original))).StatusCode);
        EqualReceipt(original, (await Accept(u, b with { SourceProof = null })).Operation);
        var r = await Read(u); Assert.Equal(kind == "attempt" ? "AttemptNotCurrent" : "CurrentScopeUnavailable", r.History[0].CurrentNg.Kind);
        if (kind == "attempt") { Assert.False(r.History[0].IsCurrentAttempt); Assert.False(r.Items.Single(x => x.TaskItemId == u.S.ItemId).HasAcceptedSelection); }
        Assert.Equal(before, await Facts(f));
    }
    [Fact] public async Task Wrong_Task_Item_Attempt_Line_and_predecessor_cannot_gain_acceptance_or_receipts() { await using var f = await PersonalSessionFixture.CreateAsync(); await Scope(f); }
    internal static async Task Scope(PersonalSessionFixture f) {
        var u = await Setup(f); var s = u.S; var b = Input(u); var first = (await Accept(u, b)).Operation; var before = await Facts(f); var c = Input(u, 2, [], first);
        foreach (var bad in new[] { c with { ExpectedTaskLineId = f.Line2 }, c with { ExpectedWorkflowVersion = 1 }, c with { ExpectedEnvironmentId = "foreign" },
            c with { Operation = c.Operation with { TaskItemId = Guid.NewGuid() } }, c with { Operation = c.Operation with { InspectionAttemptId = Guid.NewGuid() } },
            c with { Operation = c.Operation with { ContributorUserIdAssertion = s.B.User.Id } }, c with { Operation = c.Operation with { Predecessor = new(first.OperationId, new string('A', 64)) } },
            Input(u, 2), c with { Operation = c.Operation with { Predecessor = new(Guid.NewGuid(), first.ContentHash) } } })
            Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(Url(s), bad)).StatusCode);
        Assert.Equal(before, await Facts(f));
        var legacy = await f.Task(workflow: JaxPower.Ipqc.Api.Domain.WorkflowVersion.LegacyPersonalV1);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client().GetAsync(ReadUrl(s with { TaskId = legacy }))).StatusCode);
        f.Clock.Now = f.Clock.Now.AddHours(12); Assert.Equal(1, await LineSharedPlanningFixture.Generate(f));
        var other = await f.Read(db => db.PatrolTasks.Where(x => x.Id != s.TaskId && x.Id != legacy && x.WorkflowVersion == JaxPower.Ipqc.Api.Domain.WorkflowVersion.LineSharedV1).Select(x => x.Id).SingleAsync());
        var current = s with { B = await f.Login("dev-ipqa-2") }; before = await Facts(f);
        // Prove the other Task is genuinely admitted to read before checking receipt isolation.
        Assert.Equal(HttpStatusCode.OK, (await current.Client().GetAsync(ReadUrl(current with { TaskId = other }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await current.Client().GetAsync(ReceiptUrl(current with { TaskId = other }, first.OperationId))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await current.Client().PostAsJsonAsync(Url(current with { TaskId = other }), b)).StatusCode);
        Assert.Equal(before, await Facts(f));
    }
}
