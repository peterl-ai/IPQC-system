using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed class DeviceRegistryService(IpqcDbContext db, ProvisioningStore store, TimeProvider clock, DeviceEnrollmentLifecycle enrollment)
{
    public static DeviceOutput Output(RegisteredDevice x) => new(x.Id, x.DisplayName, x.Model, x.Serial, x.Status.ToString(),
        x.LineId, x.Revision, x.BindingVersion, x.ReplacedByDeviceId, Utc(x.CreatedAtUtc), Utc(x.UpdatedAtUtc),
        x.TerminalAtUtc is { } time ? Utc(time) : null, x.TerminalReason);
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    public static ProvisioningSnapshot Snapshot(RegisteredDevice x) => new(x.DisplayName, x.Revision, Model: x.Model,
        Serial: x.Serial, Status: x.Status.ToString(), LineId: x.LineId, BindingVersion: x.BindingVersion, ReplacedByDeviceId: x.ReplacedByDeviceId);
    public async Task<ProvisioningPage<DeviceOutput>> ListAsync(Guid? lineId, int page, int size, CancellationToken ct)
    {
        var query = db.RegisteredDevices.AsNoTracking().AsQueryable();
        if (lineId is { } line) query = query.Where(x => x.LineId == line);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.DisplayName).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new(rows.Select(Output).ToList(), page, size, total);
    }
    public async Task<DeviceOutput?> GetAsync(Guid id, CancellationToken ct) =>
        await db.RegisteredDevices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) is { } row ? Output(row) : null;
    public Task<DeviceOutput> CreateAsync(DeviceCreateInput input, Guid actor, string username, CancellationToken ct) => throw DeviceProvisioningRetirement.Rejected();
    public Task<DeviceOutput> UpdateAsync(Guid id, DeviceUpdateInput input, Guid actor, string username, CancellationToken ct) => throw DeviceProvisioningRetirement.Rejected();
    public Task<DeviceOutput> ReserveAsync(Guid id, DeviceBindingInput input, bool rebind, Guid actor, string username, CancellationToken ct) => throw DeviceProvisioningRetirement.Rejected();
    public Task<DeviceOutput> ReleaseAsync(Guid id, DeviceReasonInput input, bool revoke, Guid actor, string username, CancellationToken ct) => !revoke ? throw DeviceProvisioningRetirement.Rejected() : store.WriteAsync(async () =>
    {
        var row = await PendingAsync(id, input.ExpectedRevision, ct);
        var reason = ProvisioningStore.Text(input.Reason, 2000);
        if (!revoke && row.LineId is null) throw new ProvisioningException("InvalidTransition");
        var change = Guid.NewGuid();
        await enrollment.InvalidateDeviceAsync(row, revoke ? "DeviceRevoked" : "ReservationReleased", ProvisioningActor.Admin(actor, username), change, ct);
        var before = Snapshot(row); row.LineId = null;
        if (revoke) { row.Status = DeviceProvisioningStatus.Revoked; row.TerminalAtUtc = clock.GetUtcNow().UtcDateTime; row.TerminalReason = reason; }
        Advance(row, actor);
        store.Audit(null, id, revoke ? "DeviceRevoked" : "ReservationUnbound", actor, username, before, Snapshot(row), reason, change);
        return Output(row);
    }, ct);
    public Task<DeviceOutput> ReplaceAsync(Guid id, DeviceReplaceInput input, Guid actor, string username, CancellationToken ct) => throw DeviceProvisioningRetirement.Rejected();
    private async Task<RegisteredDevice> PendingAsync(Guid id, long expected, CancellationToken ct)
    {
        var row = await db.RegisteredDevices.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ProvisioningException("NotFound", 404);
        ProvisioningStore.Revision(row.Revision, expected);
        if (row.Status != DeviceProvisioningStatus.PendingCredential) throw new ProvisioningException("TerminalDevice");
        return row;
    }
    private void Advance(RegisteredDevice row, Guid actor, bool binding = true)
    {
        row.Revision = checked(row.Revision + 1); if (binding) row.BindingVersion = checked(row.BindingVersion + 1);
        row.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime; row.UpdatedByUserId = actor;
    }
}
