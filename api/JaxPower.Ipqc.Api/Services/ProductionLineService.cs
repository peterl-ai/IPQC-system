using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed class ProductionLineService(IpqcDbContext db, ProvisioningStore store)
{
    public static LineOutput Output(ProductionLine x) => new(x.Id, x.Code, x.Name, x.IsActive, x.Revision);
    public static ProvisioningSnapshot Snapshot(ProductionLine x) => new(x.Name, x.Revision, x.Code, x.IsActive);
    public async Task<ProvisioningPage<LineOutput>> ListAsync(bool? active, int page, int size, CancellationToken ct)
    {
        var query = db.ProductionLines.AsNoTracking().AsQueryable();
        if (active is { } value) query = query.Where(x => x.IsActive == value);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.NormalizedCode).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new(rows.Select(Output).ToList(), page, size, total);
    }
    public async Task<LineOutput?> GetAsync(Guid id, CancellationToken ct) =>
        await db.ProductionLines.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) is { } row ? Output(row) : null;
    public Task<LineOutput> CreateAsync(LineCreateInput input, Guid actor, string username, CancellationToken ct) => store.WriteAsync(async () =>
    {
        var row = new ProductionLine { Id = Guid.NewGuid(), Code = ProvisioningStore.Text(input.Code, 100),
            Name = ProvisioningStore.Text(input.Name, 200), IsActive = input.IsActive };
        if (await db.ProductionLines.AnyAsync(x => x.NormalizedCode == row.NormalizedCode, ct)) throw new ProvisioningException("DuplicateCode");
        db.ProductionLines.Add(row);
        store.Audit(row.Id, null, "LineCreated", actor, username, null, Snapshot(row));
        return Output(row);
    }, ct);
    public Task<LineOutput> ChangeAsync(Guid id, string? name, bool? active, long revision, Guid actor, string username, CancellationToken ct) => store.WriteAsync(async () =>
    {
        var row = await db.ProductionLines.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ProvisioningException("NotFound", 404);
        ProvisioningStore.Revision(row.Revision, revision);
        var before = Snapshot(row);
        if (active == false && await db.RegisteredDevices.AnyAsync(x => x.LineId == id, ct)) throw new ProvisioningException("LineInUse");
        if (active is { } enabled) row.IsActive = enabled;
        else row.Name = ProvisioningStore.Text(name, 200);
        row.Revision = checked(row.Revision + 1);
        store.Audit(row.Id, null, active is null ? "LineRenamed" : active.Value ? "LineActivated" : "LineDeactivated", actor, username, before, Snapshot(row));
        return Output(row);
    }, ct);
}
