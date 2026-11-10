using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class EnrollmentMigrationTests
{
    [Fact]
    public async Task Populated_P102_upgrade_preserves_all_original_columns_and_truthful_admin_audit()
    {
        await using var source = await ProvisioningFixture.CreateAsync();
        var line = await source.LineAsync("HISTORY"); await source.DeviceAsync("Synthetic historical PAD",line.Id);
        var path = source.Path + ".p102";
        try
        {
            await using var db = new IpqcDbContext(new DbContextOptionsBuilder<IpqcDbContext>().UseSqlite($"Data Source={path}").Options);
            await db.GetService<IMigrator>().MigrateAsync("20261106210701_P102DeviceRegistryProvisioning");
            await WorkflowLineMigrationTests.CopyHistoricalFactsAsync(source.Path,path);
            await CopyProvisioning(source.Path,path);
            var before=await WorkflowLineMigrationTests.CaptureAsync(path);
            await db.Database.MigrateAsync();
            Assert.Equal(before,await WorkflowLineMigrationTests.CaptureAsync(path,true));
            Assert.Empty(await db.DeviceEnrollments.ToListAsync());Assert.Empty(await db.DeviceCredentials.ToListAsync());Assert.Empty(await db.EnrollmentChallenges.ToListAsync());
            var history=await db.ProvisioningAuditEvents.ToListAsync();Assert.NotEmpty(history);
            Assert.All(history,x=>{Assert.Equal(ProvisioningActorKind.Admin,x.ActorKind);Assert.NotNull(x.ActorUserId);Assert.Null(x.ActorDeviceId);Assert.Null(x.EnrollmentId);});
            Assert.Equal(DeviceProvisioningStatus.PendingCredential,(await db.RegisteredDevices.SingleAsync()).Status);
            await using var connection=new SqliteConnection($"Data Source={path}");await connection.OpenAsync();
            await using var check=connection.CreateCommand();check.CommandText="PRAGMA foreign_key_check";
            await using var reader=await check.ExecuteReaderAsync();Assert.False(await reader.ReadAsync());
        }
        finally {SqliteConnection.ClearAllPools();File.Delete(path);}
    }

    [Fact]
    public void PostgreSQL_enrollment_script_has_native_types_restrictive_history_and_no_fabricated_actors()
    {
        using var db=new IpqcDbContext(new DbContextOptionsBuilder<IpqcDbContext>().UseNpgsql("Host=localhost;Database=sql_only;Username=unused").Options);
        var sql=db.GetService<IMigrator>().GenerateScript("20261106210701_P102DeviceRegistryProvisioning",options:MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("\"ActorUserId\" DROP NOT NULL",sql);Assert.Contains("\"ActorKind\" integer NOT NULL DEFAULT 1",sql);
        Assert.Contains("\"SourceBindingVersion\" bigint",sql);Assert.Contains("\"DeviceId\" uuid",sql);Assert.Contains("timestamp with time zone",sql);
        Assert.Contains("ON DELETE RESTRICT",sql);Assert.Contains("CK_ProvisioningAudit_Actor",sql);
        Assert.Contains("\"State\" IN (1, 2, 3, 4, 5, 6)",sql);Assert.Contains("IssuanceOperationId",sql);
        Assert.DoesNotContain("DROP TABLE",sql);Assert.DoesNotContain("UPDATE \"RegisteredDevices\"",sql);Assert.DoesNotContain("UPDATE \"Patrol",sql);
        Assert.Throws<NotSupportedException>(()=>db.GetService<IMigrator>().GenerateScript("20261109202320_P103ADeviceEnrollment","20261106210701_P102DeviceRegistryProvisioning"));
    }

    [Fact]
    public async Task Fresh_schema_rejects_forged_audit_actors_and_restricts_referenced_history_deletion()
    {
        await using var f=await EnrollmentFixture.CreateAsync();var d=await f.BoundDevice();await f.Seed(d);
        await f.Db(async db=>{
            await Assert.ThrowsAsync<SqliteException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ProvisioningAuditEvents\" SET \"ActorKind\" = 4 WHERE \"DeviceId\" = {d.Id}"));
            await Assert.ThrowsAsync<SqliteException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"RegisteredDevices\" WHERE \"Id\" = {d.Id}"));
            await Assert.ThrowsAsync<SqliteException>(()=>db.Database.ExecuteSqlRawAsync("UPDATE \"DeviceEnrollments\" SET \"TargetBindingVersion\" = \"SourceBindingVersion\""));
            await Assert.ThrowsAsync<SqliteException>(()=>db.Database.ExecuteSqlRawAsync("UPDATE \"DeviceEnrollments\" SET \"State\" = 99"));return 0;
        });
    }

    private static async Task CopyProvisioning(string source,string target)
    {
        await using var src=new SqliteConnection($"Data Source={source}");await src.OpenAsync();
        await using var dst=new SqliteConnection($"Data Source={target}");await dst.OpenAsync();
        foreach(var table in new[]{"RegisteredDevices","ProvisioningAuditEvents"})
        {
            var columns=new List<string>();await using(var info=dst.CreateCommand()){info.CommandText=$"PRAGMA table_info(\"{table}\")";await using var r=await info.ExecuteReaderAsync();while(await r.ReadAsync())columns.Add(r.GetString(1));}
            var names=string.Join(",",columns.Select(x=>$"\"{x}\""));await using var read=src.CreateCommand();read.CommandText=$"SELECT {names} FROM \"{table}\"";await using var rows=await read.ExecuteReaderAsync();
            while(await rows.ReadAsync()){await using var insert=dst.CreateCommand();insert.CommandText=$"INSERT INTO \"{table}\" ({names}) VALUES ({string.Join(",",columns.Select((_,i)=>$"@p{i}"))})";for(var i=0;i<columns.Count;i++)insert.Parameters.AddWithValue($"@p{i}",rows.GetValue(i));await insert.ExecuteNonQueryAsync();}
        }
    }
}
