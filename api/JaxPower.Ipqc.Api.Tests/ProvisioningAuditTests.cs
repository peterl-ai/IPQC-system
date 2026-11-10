using System.Net.Http.Json;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class ProvisioningAuditTests
{
    [Fact]
    public async Task Historical_reservations_and_replacement_identities_remain_in_both_line_histories_without_duplicates()
    {
        await using var f=await ProvisioningFixture.CreateAsync();var a=await f.LineAsync("A");var b=await f.LineAsync("B");var other=await f.LineAsync("OTHER");
        var old=await f.DeviceAsync("Same name");var replacement=await f.DeviceAsync("Same name");
        var s0=new ProvisioningSnapshot("Same name",1,Status:"PendingCredential",BindingVersion:1);
        var a2=s0 with{Revision=2,BindingVersion=2,LineId=a.Id};var b3=a2 with{Revision=3,BindingVersion=3,LineId=b.Id};
        var free=b3 with{Revision=4,BindingVersion=4,LineId=null};var again=free with{Revision=5,BindingVersion=5,LineId=a.Id};
        var renamed=again with{Revision=6,Name="Metadata edit"};var change=Guid.NewGuid();
        await f.Db(async db=>{
            var store=new ProvisioningStore(db,TimeProvider.System);
            void Event(Guid id,string action,ProvisioningSnapshot before,ProvisioningSnapshot after,Guid? correlation=null)=>store.Audit(null,id,action,TestAccounts.AdminId,"admin",before,after,"Historical reason",correlation);
            Event(old.Id,"ReservationBound",s0,a2);Event(old.Id,"ReservationRebound",a2,b3);Event(old.Id,"ReservationUnbound",b3,free);
            Event(old.Id,"ReservationBound",free,again);Event(old.Id,"DeviceUpdated",again,renamed);
            Event(old.Id,"DeviceReplaced",renamed,renamed with{Revision=7,BindingVersion=6,LineId=null,Status="Replaced",ReplacedByDeviceId=replacement.Id},change);
            Event(replacement.Id,"ReplacementReserved",s0,a2,change);Event(replacement.Id,"DeviceRevoked",a2,a2 with{Revision=3,BindingVersion=3,Status="Revoked",LineId=null});
            await db.SaveChangesAsync();return 0;});
        var before=await f.StateAsync();var ah=await History(f,a.Id);var bh=await History(f,b.Id);
        Assert.Equal(8,ah.Total);Assert.Equal(3,bh.Total);
        Assert.Equal(Assert.Single(ah.Items,x=>x.Action=="ReservationRebound"),Assert.Single(bh.Items,x=>x.Action=="ReservationRebound"));
        Assert.Contains(ah.Items,x=>x.Action=="ReservationBound"&&x.DeviceId==old.Id);
        Assert.Contains(bh.Items,x=>x.Action=="ReservationUnbound"&&x.Before!.LineId==b.Id);
        Assert.Contains(ah.Items,x=>x.Action=="DeviceRevoked"&&x.DeviceId==replacement.Id);
        var replaced=Assert.Single(ah.Items,x=>x.Action=="DeviceReplaced");var reserved=Assert.Single(ah.Items,x=>x.Action=="ReplacementReserved");
        Assert.Equal(change,replaced.ChangeId);Assert.Equal(change,reserved.ChangeId);Assert.Equal(old.Id,replaced.DeviceId);Assert.Equal(replacement.Id,reserved.DeviceId);
        var combined=ah.Items.Concat(bh.Items).DistinctBy(x=>x.Id).ToList();
        foreach(var item in (await f.DeviceAudit(old.Id)).Items.Concat((await f.DeviceAudit(replacement.Id)).Items).Where(x=>x.Action!="DeviceCreated"))Assert.Equal(item,Assert.Single(combined,x=>x.Id==item.Id));
        Assert.Equal(7,(await f.DeviceAudit(old.Id)).Total);Assert.Equal(3,(await f.DeviceAudit(replacement.Id)).Total);
        var all=new List<ProvisioningAuditOutput>();for(var page=1;page<=4;page++){var part=await History(f,a.Id,page,2);Assert.Equal(8,part.Total);all.AddRange(part.Items);}
        Assert.Equal(ah.Items,all);Assert.Equal(8,all.DistinctBy(x=>x.Id).Count());
        Assert.Single(ah.Items,x=>x.Action=="DeviceUpdated"&&x.Before!.LineId==a.Id&&x.After.LineId==a.Id);
        Assert.Single((await History(f,other.Id)).Items);Assert.Equal(before,await f.StateAsync());
        Assert.All(combined,x=>{Assert.Equal("Admin",x.ActorKind);Assert.Equal(TestAccounts.AdminId,x.ActorUserId);if(x.DeviceId is not null)Assert.Equal("Historical reason",x.Reason);});
    }
    private static async Task<ProvisioningPage<ProvisioningAuditOutput>> History(ProvisioningFixture f,Guid line,int page=1,int size=100)=>
        (await f.Admin.GetFromJsonAsync<ProvisioningPage<ProvisioningAuditOutput>>($"/api/admin/production-lines/{line}/audit?page={page}&pageSize={size}"))!;
}
