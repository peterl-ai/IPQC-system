using JaxPower.Ipqc.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class PhotoMigrationTests
{
    internal const string Previous="20261113143230_R3205PerItemRemoved";
    [Theory][InlineData(false)][InlineData(true)] public void Additive_provider_model_and_DDL(bool postgres) {
        var b=new DbContextOptionsBuilder<IpqcDbContext>();if(postgres) b.UseNpgsql("Host=localhost;Database=model_only;Username=unused");else b.UseSqlite("Data Source=:memory:");
        using var db=new IpqcDbContext(b.Options);
        var snapshot=db.GetService<IModelRuntimeInitializer>().Initialize(db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model,designTime:true);
        var differences=db.GetService<IMigrationsModelDiffer>().GetDifferences(snapshot.GetRelationalModel(),db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.GetRelationalModel());
        Assert.True(differences.Count==0,string.Join(";",differences.Select(d=>d.GetType().Name+":"+System.Text.Json.JsonSerializer.Serialize(d,d.GetType()))));var sql=db.GetService<IMigrator>().GenerateScript(Previous);
        foreach(var table in new[]{"InspectionAttachments","AttachmentAvailabilities","AttachmentUploads","AttachmentAcceptances","AttachmentEvents"}) Assert.Contains("CREATE TABLE \""+table+"\"",sql);
        Assert.DoesNotContain("ALTER TABLE",sql);Assert.DoesNotContain("DROP TABLE",sql);Assert.DoesNotContain("CREATE TABLE \"ContributionOperations\"",sql);
        Assert.Contains("TR_PhotoScope",sql);Assert.Contains("TR_PhotoAcceptance",sql);Assert.Contains("ON DELETE RESTRICT",sql);
        if(postgres) {Assert.Contains("\"EnvironmentId\" character varying(200)",sql);Assert.Contains("\"ByteCount\" bigint",sql);Assert.Contains("timestamp with time zone",sql);}
        Assert.Throws<NotSupportedException>(()=>db.GetService<IMigrator>().GenerateScript(db.Database.GetMigrations().Last(),Previous));
    }
    [Fact] public async Task Populated_upgrade_preserves_all_histories() {await using var p=await PhotoFixture.Create(migration:Previous);await Upgrade(p);}
    internal static async Task Upgrade(PhotoFixture p) {
        var (s,b)=await ItemRemovalMigrationTests.PriorAccepted(p.F);await ItemRemovalTests.Accept(s,ItemRemovalTests.Input(s,1));
        var before=await InspectionIdentityMigrationTests.OldFacts(p.F,true,true,true);await p.F.Edit(db=>db.Database.MigrateAsync());
        Assert.Equal(before,await InspectionIdentityMigrationTests.OldFacts(p.F,true,true,true));Assert.Equal(0,await p.F.Read(db=>db.InspectionAttachments.CountAsync()));
        Assert.Equal(1,await p.F.Read(db=>db.ItemRemovalOperations.CountAsync()));Assert.Equal(2,await p.F.Read(db=>db.ContributionOperations.CountAsync()));
        var replay=await s.Accept(b with{SourceProofs=[]});Assert.True(replay.ReplayOnly);
        if(p.F.PostgresRoot is null) await p.F.Read(async db=> {
            await db.Database.OpenConnectionAsync();await using var c=db.Database.GetDbConnection().CreateCommand();c.CommandText="PRAGMA foreign_key_check";await using var r=await c.ExecuteReaderAsync();Assert.False(await r.ReadAsync());return true;
        });
    }
    [Theory][InlineData("CREATE TABLE \"AttachmentUploads\"")][InlineData("CREATE TRIGGER \"TR_PhotoScope\"")][InlineData("INSERT INTO \"__EFMigrationsHistory\"")]
    public async Task Failed_upgrade_is_atomic(string point) {await using var p=await PhotoFixture.Create(migration:Previous);await Rollback(p,point);}
    internal static async Task Rollback(PhotoFixture p,string point) {
        var (s,_)=await ItemRemovalMigrationTests.PriorAccepted(p.F);await ItemRemovalTests.Accept(s,ItemRemovalTests.Input(s,1));var before=await InspectionIdentityMigrationTests.OldFacts(p.F,true,true,true);
        p.F.Fault.Arm(point,true);await Assert.ThrowsAsync<InvalidOperationException>(()=>p.F.Edit(db=>db.Database.MigrateAsync()));Assert.True(p.F.Fault.Entered.Task.IsCompletedSuccessfully);
        Assert.Equal(before,await InspectionIdentityMigrationTests.OldFacts(p.F,true,true,true));Assert.Equal(Previous,await p.F.Read(async db=>(await db.Database.GetAppliedMigrationsAsync()).Last()));
        await p.F.Edit(db=>db.Database.MigrateAsync());Assert.Equal(before,await InspectionIdentityMigrationTests.OldFacts(p.F,true,true,true));
    }
    [Fact] public async Task Native_immutable_and_scope_guards() {await using var p=await PhotoFixture.Create();await Integrity(p);}
    internal static async Task Integrity(PhotoFixture p) {
        var s=await SharedDraftFixture.Setup(p.F);var bytes=PhotoFixture.Image();var a=await PhotoFixture.Reserve(s,PhotoFixture.Input(s,bytes));await PhotoFixture.Ready(s,a.AttachmentId,bytes);
        async Task Reject(Func<Task> command) {
            var error=await Record.ExceptionAsync(command);Assert.NotNull(error);
            if(p.F.PostgresRoot is not null) Assert.Equal("23514",Assert.IsType<Npgsql.PostgresException>(error).SqlState);
            else Assert.Contains(Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(error).SqliteExtendedErrorCode,new[]{1811,275});
        }
        foreach(var sql in new[]{"UPDATE \"InspectionAttachments\" SET \"ExpectedBytes\"=1","DELETE FROM \"InspectionAttachments\"","UPDATE \"AttachmentAcceptances\" SET \"ByteCount\"=1","DELETE FROM \"AttachmentAcceptances\"","UPDATE \"AttachmentEvents\" SET \"State\"='Ready'","DELETE FROM \"AttachmentEvents\"","UPDATE \"AttachmentUploads\" SET \"StageKey\"='bad'","DELETE FROM \"AttachmentUploads\"","UPDATE \"AttachmentUploads\" SET \"StoredAtUtc\"=NULL","UPDATE \"AttachmentAvailabilities\" SET \"AcceptedAttachmentId\"=NULL"})
            await Reject(()=>p.F.Edit(db=>db.Database.ExecuteSqlRawAsync(sql)));
        Assert.Equal(1,await p.F.Read(db=>db.AttachmentAcceptances.CountAsync()));
        // Fresh reservation exists but SQL cannot turn it Ready without a matching accepted FK.
        var pending=await PhotoFixture.Reserve(s,PhotoFixture.Input(s,bytes));
        await Reject(()=>p.F.Edit(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AttachmentAvailabilities\" SET \"State\"='Ready' WHERE \"AttachmentId\"={pending.AttachmentId}")));
        // These are valid existing FK targets; the native source/task scope trigger rejects their combination.
        var other=await p.F.Task();var clone=Guid.NewGuid();var client=Guid.NewGuid();
        await Reject(()=>p.F.Edit(db=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"InspectionAttachments\" SELECT {clone},{client},{other},\"TaskItemId\",\"InspectionAttemptId\",\"LineId\",\"WorkflowVersion\",\"EnvironmentId\",\"SourceWorkContextId\",\"ContributorUserId\",\"ExpectedBytes\",\"ExpectedSha256\",\"DeclaredContentType\",\"CaptureSource\",{Guid.NewGuid().ToString("N")},\"CreatedAtUtc\" FROM \"InspectionAttachments\" WHERE \"Id\"={a.AttachmentId}")));
    }
}
