using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class PicRosterTests
{
    internal const string Root = "/api/admin/pic-roster";
    internal static PicCreateInput Input(string? name = "Synthetic PIC", string? department = "Synthetic Department", bool active = true) => new(Guid.NewGuid(), name, department, active);
    internal static async Task<PicMutationResult> Accepted(HttpResponseMessage response) {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        return (await response.Content.ReadFromJsonAsync<PicMutationResult>())!;
    }
    internal static async Task<string> Facts(PersonalSessionFixture f) => await BasicInformationFixture.Facts(f) + await f.Read(async db => JsonSerializer.Serialize(new {
        Identities = await db.PicRosterEntries.OrderBy(x => x.PicId).ToListAsync(),
        History = await db.PicRosterRevisions.OrderBy(x => x.PicId).ThenBy(x => x.Revision).ToListAsync()
    }));
    internal static async Task<PicLookupOutput> Lookup(DraftSetup s, string query = "") {
        var response = await s.Client().GetAsync(s.Root + "/pic-roster" + query);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var r = (await response.Content.ReadFromJsonAsync<PicLookupOutput>())!;
        Assert.False(r.CanExecute); Assert.False(r.CanSubmit); Assert.False(r.SelectionImplemented); return r;
    }
    [Fact] public async Task Global_independent_identity_lifecycle_and_history_are_truthful() { await using var f = await PersonalSessionFixture.CreateAsync(); await Lifecycle(f); }
    internal static async Task Lifecycle(PersonalSessionFixture f) {
        var s = await SharedDraftFixture.Setup(f); var admin = await f.Login("admin"); var client = f.Client(admin.AccessToken);
        var business = await BasicInformationFixture.Facts(f); var accounts = await f.Read(db => db.ApplicationUsers.CountAsync());
        var input = Input("  Synthetic PIC  "); var response = await client.PostAsJsonAsync(Root, input); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var first = (await Accepted(response)).Receipt; Assert.NotEqual(Guid.Empty, first.PicId); Assert.Equal(1, first.Revision);
        Assert.Equal("Synthetic PIC", first.Name); Assert.Equal(admin.User.Id, first.ActorUserId); Assert.Equal(admin.PersonalSessionId, first.ActorPersonalSessionId);
        Assert.Equal("admin", first.ActorUsername); Assert.Equal(DateTimeKind.Utc, first.OccurredAtUtc.Kind);
        var duplicateName = (await Accepted(await client.PostAsJsonAsync(Root, input with { RequestId = Guid.NewGuid() }))).Receipt;
        Assert.NotEqual(first.PicId, duplicateName.PicId); // Names are not identities or an arbitrary unique key.
        var edited = (await Accepted(await client.PutAsJsonAsync(Root + "/" + first.PicId, new PicEditInput(Guid.NewGuid(), 1, "Renamed synthetic PIC", "New synthetic department")))).Receipt;
        Assert.Equal(2, edited.Revision); Assert.Equal(first.PicId, edited.PicId); Assert.True(edited.IsActive);
        var inactive = (await Accepted(await client.PostAsJsonAsync(Root + $"/{first.PicId}/deactivate", new PicRevisionInput(Guid.NewGuid(), 2)))).Receipt;
        Assert.False(inactive.IsActive); Assert.DoesNotContain((await Lookup(s)).Roster.Items, x => x.PicId == first.PicId);
        var editInactive = (await Accepted(await client.PutAsJsonAsync(Root + "/" + first.PicId, new PicEditInput(Guid.NewGuid(), 3, "Still inactive PIC", "Changed department")))).Receipt;
        Assert.False(editInactive.IsActive); Assert.Equal(4, editInactive.Revision);
        var active = (await Accepted(await client.PostAsJsonAsync(Root + $"/{first.PicId}/activate", new PicRevisionInput(Guid.NewGuid(), 4)))).Receipt;
        Assert.Equal(5, active.Revision); Assert.True(active.IsActive); Assert.Equal(editInactive.Name, active.Name);
        var before = await Facts(f);
        var history = (await client.GetFromJsonAsync<PicPage<PicRevisionReceipt>>(Root + $"/{first.PicId}/history"))!;
        Assert.Equal(new[] { active, editInactive, inactive, edited, first }, history.Items);
        Assert.Equal(first, await client.GetFromJsonAsync<PicRevisionReceipt>(Root + $"/requests/{first.RequestId}/receipt"));
        Assert.Equal(2, (await Lookup(s)).Roster.Items.Count); Assert.Equal(2, (await client.GetFromJsonAsync<PicPage<PicOutput>>(Root))!.Items.Count);
        Assert.Equal(before, await Facts(f)); Assert.Equal(business, await BasicInformationFixture.Facts(f));
        Assert.Equal(accounts, await f.Read(db => db.ApplicationUsers.CountAsync()));
        Assert.DoesNotContain(first.PicId, await f.Read(db => db.ApplicationUsers.Select(x => x.Id).ToListAsync()));
        Assert.Equal(0, await f.Read(db => db.SharedDrafts.CountAsync())); Assert.Equal(0, await f.Read(db => db.PatrolTaskSubmissions.CountAsync()));
        var preflight = (await s.Client().GetFromJsonAsync<SubmitPreflightOutput>(SubmitPreflightTests.Url(s)))!;
        Assert.False(preflight.CanExecute); Assert.False(preflight.CanSubmit);
    }
    [Fact] public async Task Active_lookup_retains_actual_Task_Line_and_legacy_authority() { await using var f = await PersonalSessionFixture.CreateAsync(); await Scope(f); }
    internal static async Task Scope(PersonalSessionFixture f) {
        var s = await SharedDraftFixture.Setup(f); var admin = await f.Login("admin");
        var pic = (await Accepted(await f.Client(admin.AccessToken).PostAsJsonAsync(Root, Input()))).Receipt;
        Assert.Equal(pic.PicId, Assert.Single((await Lookup(s)).Roster.Items).PicId);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client().GetAsync(s.Root + $"/pic-roster?expectedTaskLineId={f.Line2}")).StatusCode);
        var legacy = await f.Task(workflow: WorkflowVersion.LegacyPersonalV1);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client().GetAsync($"/api/line-patrol/tasks/{legacy}/pic-roster")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client().GetAsync($"/api/line-patrol/tasks/{Guid.NewGuid()}/pic-roster")).StatusCode);
        foreach (var who in new[] { "admin", "pqe" }) Assert.Equal(HttpStatusCode.Forbidden, (await f.Client((await f.Login(who)).AccessToken).GetAsync(s.Root + "/pic-roster")).StatusCode);
        await LineSharedPlanningFixture.Plan(f, await LineSharedPlanningFixture.Standard(f), f.Line2);
        f.Clock.Now = f.Clock.Now.AddHours(12); Assert.True(await LineSharedPlanningFixture.Generate(f) > 0);
        s = s with { B = await f.Login("dev-ipqa-2") };
        var secondLineTask = (await LineSharedPlanningFixture.Tasks(f)).First(x => x.LineId == f.Line2);
        var other = (await s.Client().GetFromJsonAsync<PicLookupOutput>($"/api/line-patrol/tasks/{secondLineTask.Id}/pic-roster"))!;
        Assert.Equal(f.Line2, other.LineId); Assert.Equal(pic.PicId, Assert.Single(other.Roster.Items).PicId);
        Assert.Equal(pic.PicId, Assert.Single((await Lookup(s)).Roster.Items).PicId);
        await f.Edit(async db => (await db.ProductionLines.SingleAsync(x => x.Id == f.Line1)).IsActive = false);
        var unavailable = await s.Client().GetAsync(s.Root + "/pic-roster"); Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Contains("LineConfigurationUnavailable", await unavailable.Content.ReadAsStringAsync());
    }
    [Fact] public async Task Current_family_DB_role_and_direct_entry_are_required_without_changing_old_Admin_API() { await using var f = await PersonalSessionFixture.CreateAsync(); await Authority(f); }
    internal static async Task Authority(PersonalSessionFixture f) {
        var admin = await f.Login("admin"); var client = f.Client(admin.AccessToken); var input = Input();
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous.PostAsync(Root, new StringContent("{bad", Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous.GetAsync(Root + "?page=bad")).StatusCode);
        foreach (var who in new[] { "pqe", "dev-ipqa-1" }) {
            var other = f.Client((await f.Login(who)).AccessToken);
            foreach (var response in new[] { await other.PostAsJsonAsync(Root, input), await other.PutAsJsonAsync(Root + "/" + Guid.NewGuid(), new PicEditInput(Guid.NewGuid(), 1, "Name", "Dept")),
                await other.PostAsJsonAsync(Root + $"/{Guid.NewGuid()}/activate", new PicRevisionInput(Guid.NewGuid(), 1)), await other.PostAsJsonAsync(Root + $"/{Guid.NewGuid()}/deactivate", new PicRevisionInput(Guid.NewGuid(), 1)), await other.GetAsync(Root) }) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        var noFamily = f.Client(f.Token(admin, cs => cs.Where(c => c.Type != PersonalAccess.FamilyClaim).ToList()));
        Assert.Equal(HttpStatusCode.OK, (await noFamily.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await noFamily.PostAsJsonAsync(Root, input)).StatusCode);
        await using var scope = f.Factory.Services.CreateAsyncScope(); var service = scope.ServiceProvider.GetRequiredService<PicRosterService>();
        var principal = WorkContextFixture.Principal(admin, "Admin");
        Assert.Equal(401, (await Assert.ThrowsAsync<PersonalAccessException>(() => service.CreateAsync(null!, new ClaimsPrincipal(new ClaimsIdentity(principal.Claims)), default))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<PersonalAccessException>(() => service.CreateAsync(input with { Name = "\ud800" }, principal, default))).Status);
        await f.Edit(async db => (await db.ApplicationUsers.SingleAsync(x => x.Id == admin.User.Id)).Role = "PQE");
        Assert.Equal(403, (await Assert.ThrowsAsync<PersonalAccessException>(() => service.CreateAsync(input, principal, default))).Status);
        await f.Edit(async db => { var a = await db.ApplicationUsers.SingleAsync(x => x.Id == admin.User.Id); a.Role = "Admin"; a.MustChangePassword = true; });
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Root, input)).StatusCode);
        await f.Edit(async db => (await db.ApplicationUsers.SingleAsync(x => x.Id == admin.User.Id)).MustChangePassword = false);
        (await client.PostAsJsonAsync("/api/auth/logout", new RefreshInput(admin.RefreshToken))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root, input)).StatusCode);
        Assert.Equal(0, await f.Read(db => db.PicRosterEntries.CountAsync())); Assert.Equal(0, await f.Read(db => db.PicRosterRevisions.CountAsync()));
    }
    [Theory][InlineData("null-name")][InlineData("empty")][InlineData("space")][InlineData("long")][InlineData("control")][InlineData("null-dept")][InlineData("long-dept")][InlineData("unicode-long")]
    public async Task Invalid_text_never_creates_identity_or_audit(string kind) { await using var f = await PersonalSessionFixture.CreateAsync(); await Invalid(f, kind); }
    internal static async Task Invalid(PersonalSessionFixture f, string kind) {
        var client = f.Client((await f.Login("admin")).AccessToken); var input = Input();
        input = kind switch { "null-name" => input with { Name = null }, "empty" => input with { Name = "" }, "space" => input with { Name = "   " }, "long" => input with { Name = new('x', 201) },
            "control" => input with { Name = "bad\nname" }, "null-dept" => input with { Department = null }, "long-dept" => input with { Department = new('x', 201) }, _ => input with { Name = string.Concat(Enumerable.Repeat("\U0001F50C", 201)) } };
        var before = await Facts(f); Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Root, input)).StatusCode); Assert.Equal(before, await Facts(f));
    }
    [Fact] public async Task Strict_shape_bounds_and_noncoerced_revision_apply() { await using var f = await PersonalSessionFixture.CreateAsync(); await Shape(f); }
    internal static async Task Shape(PersonalSessionFixture f) {
        var client = f.Client((await f.Login("admin")).AccessToken); var b = Input(); var json = JsonSerializer.Serialize(b, DraftCanonical.Json); var before = await Facts(f);
        foreach (var bad in new[] { "{}", json.Replace("\"isActive\":true", "\"isActive\":\"true\""), json.Replace("\"isActive\":true", "\"x\":true"), json.Replace("\"name\":", "\"extra\":1,\"name\":"), json.Replace("\"name\":", "\"Name\":\"duplicate\",\"name\":"), json.Replace(b.RequestId.ToString(), Guid.Empty.ToString()), json.Replace("\"name\":", "\"applicationUserId\":\"" + TestAccounts.AdminId + "\",\"name\":") })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(Root, new StringContent(bad, Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(before, await Facts(f));
        PicRevisionReceipt? latest = null;
        foreach (var text in new[] { new string('x', 200), string.Concat(Enumerable.Repeat("\U0001F50C", 200)) }) latest = (await Accepted(await client.PostAsJsonAsync(Root, Input(text, text)))).Receipt;
        var editJson = JsonSerializer.Serialize(new PicEditInput(Guid.NewGuid(), 1, "Valid edit", "Department"), DraftCanonical.Json); before = await Facts(f);
        foreach (var bad in new[] { editJson.Replace("\"expectedRevision\":1", "\"expectedRevision\":\"1\""), editJson.Replace("\"expectedRevision\":1,", ""), editJson.Replace("\"name\":", "\"isActive\":true,\"name\":") })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync(Root + "/" + latest!.PicId, new StringContent(bad, Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(before, await Facts(f));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(Root + "/" + Guid.NewGuid())).StatusCode);
    }
    [Fact] public async Task Exact_retry_original_receipt_and_conflicting_identity_are_durable() { await using var f = await PersonalSessionFixture.CreateAsync(); await Replay(f); }
    internal static async Task Replay(PersonalSessionFixture f) {
        var client = f.Client((await f.Login("admin")).AccessToken); var input = Input(); var first = (await Accepted(await client.PostAsJsonAsync(Root, input))).Receipt;
        var edit = new PicEditInput(Guid.NewGuid(), 1, "New name", "New department"); var second = (await Accepted(await client.PutAsJsonAsync(Root + "/" + first.PicId, edit))).Receipt;
        var before = await Facts(f); var retry = await Accepted(await client.PostAsJsonAsync(Root, input)); Assert.True(retry.ReplayOnly); Assert.Equal(first, retry.Receipt);
        Assert.Equal(second, (await Accepted(await client.PutAsJsonAsync(Root + "/" + first.PicId, edit))).Receipt);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Root, input with { Name = "Other intent" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(Root + "/" + first.PicId, edit with { Name = "Other intent" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(Root + "/" + first.PicId, edit with { RequestId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Root + $"/{first.PicId}/activate", new PicRevisionInput(Guid.NewGuid(), 2))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(Root + "/" + first.PicId, edit with { RequestId = Guid.NewGuid(), ExpectedRevision = 2 })).StatusCode);
        Assert.Equal(before, await Facts(f));
    }
    [Fact] public async Task Unknown_committed_create_response_recovers_the_original_server_PicId() { await using var f = await PersonalSessionFixture.CreateAsync(); await Lost(f); }
    internal static async Task Lost(PersonalSessionFixture f) {
        var client = f.Client((await f.Login("admin")).AccessToken); var input = Input();
        f.CommitFault.AfterCommit = () => throw new IOException("Synthetic response loss after committed PIC creation");
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsJsonAsync(Root, input)).StatusCode);
        var before = await Facts(f); var original = await client.GetFromJsonAsync<PicRevisionReceipt>(Root + $"/requests/{input.RequestId}/receipt");
        var retry = await Accepted(await client.PostAsJsonAsync(Root, input)); Assert.True(retry.ReplayOnly); Assert.Equal(original, retry.Receipt);
        Assert.Equal(before, await Facts(f)); Assert.Equal(1, await f.Read(db => db.PicRosterEntries.CountAsync()));
    }
    [Fact] public async Task Bounded_pages_have_deterministic_identity_and_revision_order_without_GET_writes() { await using var f = await PersonalSessionFixture.CreateAsync(); await Paging(f); }
    internal static async Task Paging(PersonalSessionFixture f) {
        var client = f.Client((await f.Login("admin")).AccessToken);
        for (var i = 0; i < 5; i++) _ = await Accepted(await client.PostAsJsonAsync(Root, Input("Synthetic " + i, active: i % 2 == 0)));
        var before = await Facts(f); var all = (await client.GetFromJsonAsync<PicPage<PicOutput>>(Root))!.Items;
        var collected = new List<PicOutput>(); for (var i = 1; i <= 3; i++) { var p = (await client.GetFromJsonAsync<PicPage<PicOutput>>(Root + $"?page={i}&pageSize=2"))!; Assert.Equal(i != 3, p.HasMore); collected.AddRange(p.Items); }
        Assert.Equal(all, collected); Assert.Equal(5, collected.Select(x => x.PicId).Distinct().Count());
        Assert.Equal(2, (await client.GetFromJsonAsync<PicPage<PicOutput>>(Root + "?active=false"))!.Items.Count);
        foreach (var query in new[] { "?page=0", "?pageSize=0", "?pageSize=101", "?page=2147483647&pageSize=100" }) Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Root + query)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Root + $"/requests/{Guid.NewGuid()}/receipt")).StatusCode);
        Assert.Equal(before, await Facts(f));
    }
}
