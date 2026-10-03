using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed class PatrolStandardService(IpqcDbContext db, TimeProvider clock)
{
    public static Dictionary<string, string[]> Validate(StandardInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.PatrolStandardName)) errors[nameof(input.PatrolStandardName)] = ["Patrol Standard Name is required."];
        if (string.IsNullOrWhiteSpace(input.LineName)) errors[nameof(input.LineName)] = ["Line Name is required."];
        if (input.PatrolStandardName?.Length > 200) errors[nameof(input.PatrolStandardName)] = ["Must be 200 characters or fewer."];
        if (input.LineName?.Length > 200) errors[nameof(input.LineName)] = ["Must be 200 characters or fewer."];
        CheckLength(input.FactoryCode, 100, nameof(input.FactoryCode));
        CheckLength(input.FactoryName, 200, nameof(input.FactoryName));
        CheckLength(input.WorkshopCode, 100, nameof(input.WorkshopCode));
        CheckLength(input.LineCode, 100, nameof(input.LineCode));
        CheckLength(input.MaterialCode, 100, nameof(input.MaterialCode));
        var items = input.InspectionItems ?? [];
        if (items.Count > 1000) errors[nameof(input.InspectionItems)] = ["No more than 1000 inspection items are allowed."];
        for (var i = 0; i < items.Count; i++)
        {
            if (ItemValues(items[i]).All(string.IsNullOrWhiteSpace)) errors[$"InspectionItems[{i}]"] = ["An inspection item must contain at least one value."];
            if (ItemValues(items[i]).Any(v => v?.Length > 2000)) errors[$"InspectionItems[{i}]"] = ["Each inspection item value must be 2000 characters or fewer."];
        }
        return errors;

        void CheckLength(string? value, int max, string field)
        {
            if (value?.Length > max) errors[field] = [$"Must be {max} characters or fewer."];
        }
    }

    private static IEnumerable<string?> ItemValues(ItemInput item) =>
    [item.ProcessCode, item.ProcessName, item.InspectionItemCategory, item.InspectionItem, item.InspectionContent,
     item.UpperLimitOperator, item.UpperLimitValue, item.LowerLimitOperator, item.LowerLimitValue,
     item.InspectionType, item.SamplingPlan, item.SampleCount, item.PhotoRequirement, item.DefectLevel];

    public async Task<PagedStandards> ListAsync(string? name, string? factoryCode, string? lineCode, int page, int pageSize, CancellationToken ct)
    {
        var query = db.PatrolStandards.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(name)) query = query.Where(x => x.PatrolStandardName.Contains(name.Trim()));
        if (!string.IsNullOrWhiteSpace(factoryCode)) query = query.Where(x => x.FactoryCode == factoryCode.Trim());
        if (!string.IsNullOrWhiteSpace(lineCode)) query = query.Where(x => x.LineCode == lineCode.Trim());
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.PatrolStandardName).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Include(x => x.InspectionItems).ToListAsync(ct);
        return new PagedStandards(rows.Select(ToOutput).ToList(), page, pageSize, total);
    }

    public async Task<StandardOutput?> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await db.PatrolStandards.AsNoTracking().Include(x => x.InspectionItems)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : ToOutput(row);
    }

    public async Task<StandardOutput> CreateAsync(StandardInput input, string actor, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var row = new PatrolStandard { Id = Guid.NewGuid(), CreatedBy = actor, UpdatedBy = actor,
            CreatedAtUtc = now, UpdatedAtUtc = now };
        ApplyHeader(row, input);
        row.InspectionItems = BuildItems(input.InspectionItems);
        db.PatrolStandards.Add(row);
        await db.SaveChangesAsync(ct);
        return ToOutput(row);
    }

    public async Task<StandardOutput?> UpdateAsync(Guid id, StandardInput input, string actor, CancellationToken ct)
    {
        var row = await db.PatrolStandards.Include(x => x.InspectionItems).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return null;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        ApplyHeader(row, input);
        row.UpdatedBy = actor;
        row.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        db.PatrolStandardItems.RemoveRange(row.InspectionItems);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        var items = BuildItems(input.InspectionItems);
        foreach (var item in items) item.PatrolStandardId = id;
        db.PatrolStandardItems.AddRange(items);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var row = await db.PatrolStandards.FindAsync([id], ct);
        if (row is null) return false;
        db.PatrolStandards.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<StandardOutput?> CopyAsync(Guid id, string? name, string actor, CancellationToken ct)
    {
        var source = await GetAsync(id, ct);
        if (source is null) return null;
        const string suffix = " (Copy)";
        var copiedName = name ?? $"{source.PatrolStandardName[..Math.Min(source.PatrolStandardName.Length, 200 - suffix.Length)]}{suffix}";
        var input = new StandardInput(copiedName, source.FactoryCode,
            source.FactoryName, source.WorkshopCode, source.LineCode, source.LineName, source.MaterialCode,
            source.InspectionItems.Select(x => new ItemInput(x.ProcessCode, x.ProcessName, x.InspectionItemCategory,
                x.InspectionItem, x.InspectionContent, x.UpperLimitOperator, x.UpperLimitValue,
                x.LowerLimitOperator, x.LowerLimitValue, x.InspectionType, x.SamplingPlan, x.SampleCount,
                x.PhotoRequirement, x.DefectLevel)).ToList());
        return await CreateAsync(input, actor, ct);
    }

    private static List<PatrolStandardItem> BuildItems(List<ItemInput>? items) =>
        (items ?? []).Select((x, i) => new PatrolStandardItem {
            Id = Guid.NewGuid(), SequenceNo = i + 1, ProcessCode = Clean(x.ProcessCode), ProcessName = Clean(x.ProcessName),
            InspectionItemCategory = Clean(x.InspectionItemCategory), InspectionItem = Clean(x.InspectionItem),
            InspectionContent = Clean(x.InspectionContent), UpperLimitOperator = Clean(x.UpperLimitOperator),
            UpperLimitValue = Clean(x.UpperLimitValue), LowerLimitOperator = Clean(x.LowerLimitOperator),
            LowerLimitValue = Clean(x.LowerLimitValue), InspectionType = Clean(x.InspectionType),
            SamplingPlan = Clean(x.SamplingPlan), SampleCount = Clean(x.SampleCount),
            PhotoRequirement = Clean(x.PhotoRequirement), DefectLevel = Clean(x.DefectLevel),
        }).ToList();

    private static void ApplyHeader(PatrolStandard row, StandardInput input)
    {
        row.PatrolStandardName = Clean(input.PatrolStandardName);
        row.FactoryCode = Clean(input.FactoryCode);
        row.FactoryName = Clean(input.FactoryName);
        row.WorkshopCode = Clean(input.WorkshopCode);
        row.LineCode = Clean(input.LineCode);
        row.LineName = Clean(input.LineName);
        row.MaterialCode = Clean(input.MaterialCode);
    }

    private static string Clean(string? value) => value?.Trim() ?? "";

    private static StandardOutput ToOutput(PatrolStandard row) => new(row.Id, row.PatrolStandardName,
        row.FactoryCode, row.FactoryName, row.WorkshopCode, row.LineCode, row.LineName, row.MaterialCode,
        row.CreatedBy, row.CreatedAtUtc, row.UpdatedBy, row.UpdatedAtUtc,
        row.InspectionItems.OrderBy(x => x.SequenceNo).Select(x => new ItemOutput(x.Id, x.SequenceNo,
            x.ProcessCode, x.ProcessName, x.InspectionItemCategory, x.InspectionItem,
            x.InspectionContent, x.UpperLimitOperator, x.UpperLimitValue, x.LowerLimitOperator,
            x.LowerLimitValue, x.InspectionType, x.SamplingPlan, x.SampleCount,
            x.PhotoRequirement, x.DefectLevel)).ToList());
}
