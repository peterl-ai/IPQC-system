using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace JaxPower.Ipqc.Api.Services;

public sealed class ConfiguredLineDirectory(IpqcDbContext db, PersonalAccessOptions options)
{
    public async Task<List<PersonalLine>> LinesAsync(CancellationToken ct)
    {
        var ids = options.ConfiguredLines();
        var rows = await db.ProductionLines.AsNoTracking().Where(x => ids.Contains(x.Id) && x.IsActive).ToListAsync(ct);
        if (rows.Count != 2) throw new PersonalAccessException("LineConfigurationUnavailable", 503);
        return ids.Select((id, i) => { var row = rows.Single(x => x.Id == id); return new PersonalLine(id, row.Code, row.Name, i + 1); }).ToList();
    }
}
