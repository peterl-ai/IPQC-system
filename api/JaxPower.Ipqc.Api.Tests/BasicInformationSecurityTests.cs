using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.BasicInformationFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class BasicInformationSecurityTests
{
    [Fact] public async Task Current_family_DB_role_and_proof_separation_apply_to_HTTP_and_direct_entry() { await using var f = await PersonalSessionFixture.CreateAsync(); await Authority(f); }
    internal static async Task Authority(PersonalSessionFixture f) {
        var s = await SharedDraftFixture.Setup(f); var b = Input(s); var url = WriteUrl(s);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous.PostAsync(url, new StringContent("{broken", Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous.GetAsync(ReadUrl(s) + "?limit=bad")).StatusCode);
        foreach (var role in new[] { "admin", "pqe" }) {
            var actor = await f.Login(role); Assert.Equal(HttpStatusCode.Forbidden, (await s.Client(actor).PostAsJsonAsync(url, b)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await s.Client(actor).GetAsync(ReadUrl(s))).StatusCode);
        }
        var noFamily = f.Token(s.B, cs => cs.Where(c => c.Type != PersonalAccess.FamilyClaim).ToList());
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client(noFamily).PostAsJsonAsync(url, b)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client(s.Source.ProofSecret).PostAsJsonAsync(url, b)).StatusCode);
        await using (var scope = f.Factory.Services.CreateAsyncScope()) {
            var service = scope.ServiceProvider.GetRequiredService<SharedDraftService>();
            var fake = new ClaimsPrincipal(new ClaimsIdentity(WorkContextFixture.Principal(s.B).Claims));
            Assert.Equal(401, (await Assert.ThrowsAsync<PersonalAccessException>(() => service.AcceptBasicInformationAsync(s.TaskId, null!, fake, default))).Status);
            Assert.Equal(401, (await Assert.ThrowsAsync<PersonalAccessException>(() => service.ReadBasicInformationAsync(s.TaskId, fake, default))).Status);
            Assert.Equal(401, (await Assert.ThrowsAsync<PersonalAccessException>(() => service.BasicInformationReceiptAsync(s.TaskId, b.Operation.OperationId, fake, default))).Status);
            var admin = await f.Login("admin");
            Assert.Equal(403, (await Assert.ThrowsAsync<PersonalAccessException>(() => service.AcceptBasicInformationAsync(s.TaskId, b, WorkContextFixture.Principal(admin, "IPQA"), default))).Status);
            var invalid = b with { Operation = b.Operation with { Value = "\ud800" } };
            Assert.Equal(400, (await Assert.ThrowsAsync<PersonalAccessException>(() => service.AcceptBasicInformationAsync(s.TaskId, invalid, WorkContextFixture.Principal(s.B), default))).Status);
        }
        await f.Edit(async db => (await db.ApplicationUsers.SingleAsync(x => x.Id == s.B.User.Id)).MustChangePassword = true);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client().PostAsJsonAsync(url, b)).StatusCode);
        await f.Edit(async db => (await db.ApplicationUsers.SingleAsync(x => x.Id == s.B.User.Id)).MustChangePassword = false);
        (await s.Client().PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.B.RefreshToken))).EnsureSuccessStatusCode();
        var before = await Facts(f); Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client().GetAsync(ReadUrl(s))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client().PostAsJsonAsync(url, b)).StatusCode); Assert.Equal(before, await Facts(f)); await Counts(s, 0, 0, 0);
    }
    [Fact] public async Task Named_field_explicit_value_and_closed_manifest_cannot_be_omitted_or_coerced() { await using var f = await PersonalSessionFixture.CreateAsync(); await Shape(f); }
    internal static async Task Shape(PersonalSessionFixture f) {
        var s = await SharedDraftFixture.Setup(f); var b = Input(s); var text = JsonSerializer.Serialize(b, DraftCanonical.Json); var before = await Facts(f);
        var missingValue = JsonNode.Parse(text)!; missingValue["operation"]!.AsObject().Remove("value");
        var missingField = JsonNode.Parse(text)!; missingField["operation"]!.AsObject().Remove("field");
        var numericField = JsonNode.Parse(text)!; numericField["operation"]!["field"] = 0;
        foreach (var bad in new[] { missingValue.ToJsonString(), missingField.ToJsonString(), numericField.ToJsonString(),
            text.Replace("\"Customer\"", "\"Unknown\""), text.Replace("\"Customer\"", "\"customer\""),
            text.Replace("\"value\":\"Synthetic customer\"", "\"value\":\"Synthetic customer\",\"VALUE\":null"),
            text.Replace("\"originLocalEditVersion\":1", "\"originLocalEditVersion\":\"1\""),
            text[..^1] + ",\"customer\":\"implicit replacement\"}", text.Replace("\"value\":\"Synthetic customer\"", "\"value\":false") }) {
            Assert.NotEqual(text, bad);
            Assert.Equal(HttpStatusCode.BadRequest, (await s.Client().PostAsync(WriteUrl(s), new StringContent(bad, Encoding.UTF8, "application/json"))).StatusCode);
        }
        Assert.Equal(before, await Facts(f)); await Counts(s, 0, 0, 0);
    }
    [Fact] public async Task Actual_Task_Line_workflow_and_legacy_guards_remain_authoritative() { await using var f = await PersonalSessionFixture.CreateAsync(); await Scope(f); }
    internal static async Task Scope(PersonalSessionFixture f) {
        var s = await SharedDraftFixture.Setup(f); var b = Input(s); var before = await Facts(f);
        foreach (var bad in new[] { b with { ExpectedTaskLineId = f.Line2 }, b with { ExpectedWorkflowVersion = 1 }, b with { ExpectedEnvironmentId = "foreign environment" },
            b with { Operation = b.Operation with { ContributorUserIdAssertion = s.B.User.Id } } })
            Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(WriteUrl(s), bad)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().GetAsync(ReadUrl(s) + "?expectedTaskLineId=" + f.Line2)).StatusCode);
        // A genuine generated shared Task is ownerless; the existing legacy owner guard rejects it first.
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client().PutAsJsonAsync($"/api/patrol-tasks/{s.TaskId}/draft", new TaskDraftInput("Day", []))).StatusCode);
        Assert.Equal(before, await Facts(f));
        var legacy = await f.Task(workflow: WorkflowVersion.LegacyPersonalV1); before = await Facts(f);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client().PostAsJsonAsync(WriteUrl(s with { TaskId = legacy }), b)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client().GetAsync(ReadUrl(s with { TaskId = legacy }))).StatusCode); Assert.Equal(before, await Facts(f));
        f.Clock.Now = f.Clock.Now.AddHours(12); Assert.Equal(1, await LineSharedPlanningFixture.Generate(f));
        var other = await f.Read(db => db.PatrolTasks.Where(x => x.Id != s.TaskId && x.Id != legacy && x.WorkflowVersion == WorkflowVersion.LineSharedV1).Select(x => x.Id).SingleAsync());
        s = s with { A = await f.Login(), B = await f.Login("dev-ipqa-2") };
        var accepted = (await Accept(s, b)).Operation; before = await Facts(f);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().PostAsJsonAsync(WriteUrl(s with { TaskId = other }), b)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client().GetAsync(ReceiptUrl(s with { TaskId = other }, accepted.OperationId))).StatusCode); Assert.Equal(before, await Facts(f));
    }
    [Theory][InlineData("logout")][InlineData("self-password")][InlineData("expiry")][InlineData("refresh")]
    public async Task Queued_A_header_work_survives_normal_session_changes(string action) { await using var f = await PersonalSessionFixture.CreateAsync(); await Normal(f, action); }
    internal static async Task Normal(PersonalSessionFixture f, string action) {
        var s = await SharedDraftFixture.Setup(f); var first = (await Accept(s, Input(s))).Operation; var queued = Input(s, 1, BasicInformationField.Production, "Queued A text");
        if (action == "logout") (await s.Client(s.A).PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.A.RefreshToken))).EnsureSuccessStatusCode();
        if (action == "self-password") (await s.Client(s.A).PostAsJsonAsync("/api/auth/change-password", new ChangePasswordInput(TestAccounts.Password, "SyntheticChanged123A"))).EnsureSuccessStatusCode();
        if (action == "refresh") _ = await f.Refresh(s.A.RefreshToken!);
        if (action == "expiry") { f.Clock.Now = f.Clock.Now.AddDays(15); s = s with { B = await f.Login("dev-ipqa-2") }; }
        var accepted = (await Accept(s, queued)).Operation; Assert.Equal(s.A.User.Id, accepted.ContributorUserId); Assert.Equal(s.B.PersonalSessionId, accepted.ActorPersonalSessionId);
        Assert.Null(await f.Read(db => db.WorkContexts.Where(x => x.Id == s.Source.Context.Id).Select(x => x.SecurityRevokedAtUtc).SingleAsync()));
        Assert.Equal(first, await s.Client().GetFromJsonAsync<BasicInformationReceipt>(ReceiptUrl(s, first.OperationId))); await Counts(s, 2, 2, 2);
    }
    [Theory][InlineData("reset")][InlineData("role")][InlineData("deactivate")][InlineData("explicit")]
    public async Task Sticky_security_revocation_blocks_new_work_and_keeps_accepted_history(string action) { await using var f = await PersonalSessionFixture.CreateAsync(); await Security(f, action); }
    internal static async Task Security(PersonalSessionFixture f, string action) {
        var s = await SharedDraftFixture.Setup(f); var original = Input(s); var first = (await Accept(s, original)).Operation;
        var queued = Input(s, 1, BasicInformationField.Production); await SharedDraftFixture.Revoke(s, action);
        if (action is "role" or "deactivate") {
            var admin = await f.Login("admin"); var legacy = f.Client(f.Token(admin, cs => cs.Where(c => c.Type != PersonalAccess.FamilyClaim).ToList()));
            var restore = action == "role" ? await legacy.PutAsJsonAsync($"/api/users/{s.A.User.Id}", new UserUpdateInput("Synthetic inspector", "IPQA")) : await legacy.PostAsync($"/api/users/{s.A.User.Id}/activate", null);
            Assert.True(restore.IsSuccessStatusCode, await restore.Content.ReadAsStringAsync());
        }
        var before = await Facts(f); var bad = await s.Client().PostAsJsonAsync(WriteUrl(s), queued); Assert.Equal(HttpStatusCode.Conflict, bad.StatusCode);
        var detail = JsonDocument.Parse(await bad.Content.ReadAsStringAsync()).RootElement; Assert.Equal("SourceSecurityRevoked", detail.GetProperty("code").GetString());
        Assert.True(detail.GetProperty("quarantineRequired").GetBoolean()); Assert.Equal(queued.Operation.OperationId, detail.GetProperty("operationIds")[0].GetGuid());
        Assert.Equal(first, (await Accept(s, original with { SourceProof = null })).Operation); Assert.Equal(before, await Facts(f));
        var sourceB = await WorkContextFixture.Issue(f, s.TaskId, s.B);
        var corrected = (await Accept(s, Input(s, 1, value: "New B correction", previous: first, source: sourceB))).Operation;
        Assert.Equal(s.B.User.Id, corrected.ContributorUserId); Assert.Equal(first, await s.Client().GetFromJsonAsync<BasicInformationReceipt>(ReceiptUrl(s, first.OperationId)));
        Assert.NotNull(await f.Read(db => db.WorkContexts.Where(x => x.Id == s.Source.Context.Id).Select(x => x.SecurityRevokedAtUtc).SingleAsync())); await Counts(s, 2, 2, 1);
    }
    [Theory][InlineData("snapshot")][InlineData("attempt")]
    public async Task Unsupported_current_scope_cannot_accept_new_headers_but_does_not_erase_old_receipts(string kind) { await using var f = await PersonalSessionFixture.CreateAsync(); await Unavailable(f, kind); }
    internal static async Task Unavailable(PersonalSessionFixture f, string kind) {
        var s = await SharedDraftFixture.Setup(f); var original = Input(s); var first = (await Accept(s, original)).Operation;
        if (kind == "snapshot") await f.Edit(async db => (await db.PatrolTasks.SingleAsync(x => x.Id == s.TaskId)).PlanningSnapshotJson = "{invalid");
        else await f.Edit(db => db.InspectionAttemptApplicabilities.Where(x => x.PatrolTaskItemId == s.ItemId).ExecuteDeleteAsync());
        var before = await Facts(f); var result = await s.Client().PostAsJsonAsync(WriteUrl(s), Input(s, 1, BasicInformationField.Production));
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode); Assert.Contains("DraftAcceptanceUnavailable", await result.Content.ReadAsStringAsync());
        Assert.Equal(first, (await Accept(s, original with { SourceProof = null })).Operation);
        Assert.Equal(first, await s.Client().GetFromJsonAsync<BasicInformationReceipt>(ReceiptUrl(s, first.OperationId)));
        var read = await Read(s); Assert.Equal(first, read.Fields[0].LastOperation); Assert.DoesNotContain(read.Availability, new[] { "FoundationOnly", "DraftFoundation" });
        Assert.Equal(before, await Facts(f)); await Counts(s, 1, 1, 1);
    }
}
