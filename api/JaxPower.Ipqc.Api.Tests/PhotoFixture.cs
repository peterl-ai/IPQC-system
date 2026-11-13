using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

internal sealed class PhotoFault : IProtectedPhotoStore,IPhotoDurability
{
    public required ProtectedPhotoStore Inner;
    public Func<Task>? AfterStage;
    public Action? BeforeStage,BeforePromote,AfterPromote;
    public void CheckConfigured()=>Inner.CheckConfigured();
    public async Task Stage(string key,Stream body,long length,CancellationToken ct) {BeforeStage?.Invoke();await Inner.Stage(key,body,length,ct);if(AfterStage is {} hook) await hook();}
    public Task<byte[]?> Read(string key,bool final,CancellationToken ct)=>Inner.Read(key,final,ct);
    public void Promote(string stage,string final) {BeforePromote?.Invoke();Inner.Promote(stage,final);AfterPromote?.Invoke();}
    public void FlushExisting(string key,bool final)=>Inner.FlushExisting(key,final);
}
internal sealed class PhotoFixture : IAsyncDisposable
{
    public required PersonalSessionFixture F;
    public required string DirectoryPath;
    public required PhotoFault Fault;
    public static async Task<PhotoFixture> Create(bool postgres=false,string? migration=null,string? environment=PersonalSessionFixture.Environment)
    {
        var path=Path.Combine(Path.GetTempPath(),"ipqc-photo-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);
        if(OperatingSystem.IsLinux()) File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        else if(OperatingSystem.IsWindows()) {
            using var current=System.Security.Principal.WindowsIdentity.GetCurrent();var acl=new System.Security.AccessControl.DirectorySecurity();acl.SetAccessRuleProtection(true,false);
            foreach(var sid in new[]{current.User!,new System.Security.Principal.SecurityIdentifier("S-1-5-18"),new System.Security.Principal.SecurityIdentifier("S-1-5-32-544")})
                acl.AddAccessRule(new(sid,System.Security.AccessControl.FileSystemRights.FullControl,System.Security.AccessControl.InheritanceFlags.ContainerInherit|System.Security.AccessControl.InheritanceFlags.ObjectInherit,System.Security.AccessControl.PropagationFlags.None,System.Security.AccessControl.AccessControlType.Allow));
            System.IO.FileSystemAclExtensions.SetAccessControl(new DirectoryInfo(path),acl);
        }
        PhotoFault? fault=null;
        var f=await PersonalSessionFixture.CreateAsync(postgres,environment:environment,migration:migration,configure:services=>
            services.AddSingleton<IProtectedPhotoStore>(sp=>fault=new PhotoFault {Inner=new ProtectedPhotoStore(sp.GetRequiredService<IConfiguration>(),sp.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>())}));
        f.Factory.Services.GetRequiredService<IConfiguration>()["InspectionPhotos:Root"]=path;
        _=f.Factory.Services.GetRequiredService<IProtectedPhotoStore>();
        return new(){F=f,DirectoryPath=path,Fault=fault!};
    }
    public static byte[] Image(bool jpeg=false,int width=3,int height=2)
    {
        using var bitmap=new SKBitmap(width,height);bitmap.Erase(SKColors.Coral);using var image=SKImage.FromBitmap(bitmap);
        using var data=image.Encode(jpeg?SKEncodedImageFormat.Jpeg:SKEncodedImageFormat.Png,90);return data.ToArray();
    }
    public static AttachmentInput Input(DraftSetup s,byte[] bytes,string type="image/png")=>new(Guid.NewGuid(),1,s.ItemId,s.AttemptId,s.F.Line1,2,
        s.F.Options.ValidEnvironment!,s.Source.Context.Id,s.Source.Context.UserId,s.Source.ProofSecret,bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)),type,"Camera");
    public static string Root(DraftSetup s)=>s.Root+"/inspection-attachments";
    public static async Task<AttachmentOutput> Reserve(DraftSetup s,AttachmentInput i)
    {
        var r=await s.Client().PostAsJsonAsync(Root(s),i);Assert.True(r.IsSuccessStatusCode,await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<AttachmentOutput>())!;
    }
    public static Task<HttpResponseMessage> Upload(DraftSetup s,Guid id,byte[] bytes,AuthOutput? user=null,string? proof=null)
    {
        var request=new HttpRequestMessage(HttpMethod.Put,Root(s)+$"/{id}/content") {Content=new ByteArrayContent(bytes)};
        request.Content.Headers.ContentType=new MediaTypeHeaderValue("application/octet-stream");request.Headers.Add("X-WorkContext-Proof",proof??s.Source.ProofSecret);
        return s.Client(user).SendAsync(request);
    }
    public static Task<HttpResponseMessage> Recover(DraftSetup s,Guid id,string? proof=null)
    {
        var request=new HttpRequestMessage(HttpMethod.Post,Root(s)+$"/{id}/recover");request.Headers.Add("X-WorkContext-Proof",proof??s.Source.ProofSecret);return s.Client().SendAsync(request);
    }
    public static async Task<AttachmentOutput> Ready(DraftSetup s,Guid id,byte[] bytes)
    {
        var r=await Upload(s,id,bytes);Assert.True(r.IsSuccessStatusCode,await r.Content.ReadAsStringAsync());return (await r.Content.ReadFromJsonAsync<AttachmentOutput>())!;
    }
    public Task<string> FinalPath(Guid id)=>F.Read(async db=>Path.Combine(DirectoryPath,(await db.InspectionAttachments.SingleAsync(x=>x.Id==id)).StorageKey+".blob"));
    public async ValueTask DisposeAsync()
    {
        await F.DisposeAsync();
        var path=Path.GetFullPath(DirectoryPath);var parent=Path.GetFullPath(Path.GetTempPath());
        if(Path.GetDirectoryName(path)!=Path.TrimEndingDirectorySeparator(parent) || !Path.GetFileName(path).StartsWith("ipqc-photo-test-",StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test cleanup path");
        Directory.Delete(path,true);
    }
}
