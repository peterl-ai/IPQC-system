using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class ProvisioningAuditTests
{
    [Fact]
    public async Task Line_history_includes_reservation_lifecycle_and_both_replacement_identities_without_changing_device_history()
    {
        await using var app = await ProvisioningFixture.CreateAsync();
        var a = await app.LineAsync("A"); var b = await app.LineAsync("B"); var unrelated = await app.LineAsync("Unrelated");
        var rename = await app.Admin.PutAsJsonAsync($"/api/admin/production-lines/{a.Id}", new LineRenameInput("Renamed", a.Revision));
        rename.EnsureSuccessStatusCode();
        var lineLifecycle = (await LineAudit(app, a.Id)).Items;
        Assert.Equal(2, lineLifecycle.Count);
        Assert.All(lineLifecycle, x => Assert.Null(x.DeviceId));

        // Duplicated names must never serve as device identities in line history.
        var old = await app.DeviceAsync("Same display name"); var replacement = await app.DeviceAsync("Same display name");
        var oldId = old.Id;
        old = await app.DeviceAction(old, "bind", new DeviceBindingInput(a.Id, old.Revision));
        var initialBind = Assert.Single((await LineAudit(app, a.Id)).Items, x => x.Action == "ReservationBound");
        Assert.Equal(oldId, initialBind.DeviceId); Assert.Equal(a.Id, initialBind.After.LineId);

        old = await app.DeviceAction(old, "rebind", new DeviceBindingInput(b.Id, old.Revision, "Move"));
        var movedFrom = Assert.Single((await LineAudit(app, a.Id)).Items, x => x.Action == "ReservationRebound");
        var movedTo = Assert.Single((await LineAudit(app, b.Id)).Items, x => x.Action == "ReservationRebound");
        Assert.Equal(movedFrom, movedTo); Assert.Equal(a.Id, movedFrom.Before!.LineId); Assert.Equal(b.Id, movedTo.After.LineId);
        Assert.Equal("Move", movedTo.Reason); Assert.Equal(oldId, movedTo.DeviceId);

        old = await app.DeviceAction(old, "unbind", new DeviceReasonInput(old.Revision, "Release"));
        var released = Assert.Single((await LineAudit(app, b.Id)).Items, x => x.Action == "ReservationUnbound");
        Assert.Equal(b.Id, released.Before!.LineId); Assert.Null(released.After.LineId);
        Assert.DoesNotContain((await LineAudit(app, a.Id)).Items, x => x.Id == released.Id);

        old = await app.DeviceAction(old, "bind", new DeviceBindingInput(a.Id, old.Revision));
        replacement = await app.DeviceAction(old, "replace", new DeviceReplaceInput(replacement.Id, old.Revision, replacement.Revision, "Replace hardware"));
        replacement = await app.DeviceAction(replacement, "revoke", new DeviceReasonInput(replacement.Revision, "Retire"));
        var beforeReads = await app.StateAsync();
        var aHistory = await LineAudit(app, a.Id); var bHistory = await LineAudit(app, b.Id);
        Assert.Equal(8, aHistory.Total); Assert.Equal(3, bHistory.Total);
        var replaced = Assert.Single(aHistory.Items, x => x.Action == "DeviceReplaced");
        var reserved = Assert.Single(aHistory.Items, x => x.Action == "ReplacementReserved");
        Assert.Equal(oldId, replaced.DeviceId); Assert.Equal(replacement.Id, reserved.DeviceId);
        Assert.Equal(replaced.ChangeId, reserved.ChangeId);
        Assert.Equal(replacement.Id, replaced.After.ReplacedByDeviceId);
        Assert.Equal(replaced.Before!.Name, reserved.After.Name);
        Assert.Equal("Replace hardware", replaced.Reason); Assert.Equal(replaced.Reason, reserved.Reason);
        var revoked = Assert.Single(aHistory.Items, x => x.Action == "DeviceRevoked");
        Assert.Equal(replacement.Id, revoked.DeviceId); Assert.Equal(a.Id, revoked.Before!.LineId); Assert.Null(revoked.After.LineId);

        var oldHistory = await app.DeviceAudit(oldId); var newHistory = await app.DeviceAudit(replacement.Id);
        Assert.Equal(6, oldHistory.Total); Assert.Equal(3, newHistory.Total);
        Assert.All(oldHistory.Items, x => Assert.Equal(oldId, x.DeviceId));
        Assert.All(newHistory.Items, x => Assert.Equal(replacement.Id, x.DeviceId));
        var lineEvents = aHistory.Items.Concat(bHistory.Items).DistinctBy(x => x.Id).ToList();
        foreach (var entry in oldHistory.Items.Concat(newHistory.Items).Where(x => x.Action != "DeviceCreated"))
            Assert.Equal(entry, Assert.Single(lineEvents, x => x.Id == entry.Id));
        foreach (var entry in lineLifecycle) Assert.Equal(entry, Assert.Single(aHistory.Items, x => x.Id == entry.Id));
        Assert.All(lineEvents, x =>
        {
            Assert.Equal(TestAccounts.AdminId, x.ActorUserId); Assert.Equal("admin", x.ActorUsername);
            Assert.Equal(DateTimeKind.Utc, x.OccurredAtUtc.Kind);
        });
        var untouched = await LineAudit(app, unrelated.Id);
        Assert.Equal(1, untouched.Total); Assert.Equal("LineCreated", Assert.Single(untouched.Items).Action);
        Assert.Equal(beforeReads, await app.StateAsync());
    }

    [Fact]
    public async Task Line_audit_pagination_counts_each_event_once_even_when_both_snapshots_reference_the_line()
    {
        await using var app = await ProvisioningFixture.CreateAsync();
        var line = await app.LineAsync("A"); var device = await app.DeviceAsync("PAD"); var replacement = await app.DeviceAsync("PAD");
        device = await app.DeviceAction(device, "bind", new DeviceBindingInput(line.Id, device.Revision));
        var update = await app.Admin.PutAsJsonAsync($"/api/admin/devices/{device.Id}", new DeviceUpdateInput("Renamed PAD", "Model", "Description", device.Revision));
        update.EnsureSuccessStatusCode(); device = (await update.Content.ReadFromJsonAsync<DeviceOutput>())!;
        await app.DeviceAction(device, "replace", new DeviceReplaceInput(replacement.Id, device.Revision, replacement.Revision, "Replace"));
        var all = await LineAudit(app, line.Id);
        Assert.Equal(5, all.Total);
        var metadata = Assert.Single(all.Items, x => x.Action == "DeviceUpdated");
        Assert.Equal(line.Id, metadata.Before!.LineId); Assert.Equal(line.Id, metadata.After.LineId);
        var collected = new List<ProvisioningAuditOutput>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await LineAudit(app, line.Id, page, 2);
            Assert.Equal(5, result.Total); Assert.Equal(page, result.Page); Assert.Equal(2, result.PageSize);
            Assert.Equal(page == 3 ? 1 : 2, result.Items.Count); collected.AddRange(result.Items);
        }
        Assert.Equal(all.Items, collected);
        Assert.Equal(5, collected.Select(x => x.Id).Distinct().Count());
        var empty = await LineAudit(app, line.Id, 4, 2);
        Assert.Equal(5, empty.Total); Assert.Empty(empty.Items);
    }

    private static async Task<ProvisioningPage<ProvisioningAuditOutput>> LineAudit(ProvisioningFixture app, Guid line, int page = 1, int size = 100) =>
        (await app.Admin.GetFromJsonAsync<ProvisioningPage<ProvisioningAuditOutput>>($"/api/admin/production-lines/{line}/audit?page={page}&pageSize={size}"))!;
}
