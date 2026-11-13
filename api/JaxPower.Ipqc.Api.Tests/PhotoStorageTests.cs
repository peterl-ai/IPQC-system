using JaxPower.Ipqc.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class PhotoStorageTests
{
    [Fact] public async Task Native_nonoverwriting_roundtrip() {
        await using var p=await PhotoFixture.Create();var bytes=PhotoFixture.Image();
        for(var n=0;n<25;n++) {
            var stage=Guid.NewGuid().ToString("N");var final=Guid.NewGuid().ToString("N");
            await p.Fault.Inner.Stage(stage,new MemoryStream(bytes),bytes.Length,default);
            Assert.Equal(bytes,await p.Fault.Inner.Read(stage,false,default));
            p.Fault.Inner.Promote(stage,final);
            Assert.True(File.Exists(Path.Combine(p.DirectoryPath,final+".blob")),"Expected "+final+".blob; files="+string.Join(',',Directory.GetFiles(p.DirectoryPath).Select(Path.GetFileName)));
            Assert.Equal(bytes,await p.Fault.Inner.Read(final,true,default));
            var other=Guid.NewGuid().ToString("N");await p.Fault.Inner.Stage(other,new MemoryStream(new byte[]{7}),1,default);
            Assert.Throws<PhotoStorageException>(()=>p.Fault.Inner.Promote(other,final));
            Assert.Equal(bytes,await p.Fault.Inner.Read(final,true,default));Assert.Equal(new byte[]{7},await p.Fault.Inner.Read(other,false,default));
        }
    }
    [Fact] public async Task Opaque_keys_reject_paths() {
        await using var p=await PhotoFixture.Create();
        foreach(var key in new[]{"../escape","/tmp/path",@"C:\path","","a/b",new string('a',31),new string('A',32),new string('.',32)})
            await Assert.ThrowsAsync<PhotoStorageException>(()=>p.Fault.Inner.Read(key,true,default));
        Assert.Empty(Directory.GetFiles(p.DirectoryPath));
    }
    [LinuxPhotoFact] public async Task Symlink_root_and_leaf_fail_closed() {
        if(!OperatingSystem.IsLinux()) throw new InvalidOperationException("Linux-only test must be skipped on other platforms.");
        await using var p=await PhotoFixture.Create();var s=await SharedDraftFixture.Setup(p.F);var bytes=PhotoFixture.Image();
        var a=await PhotoFixture.Reserve(s,PhotoFixture.Input(s,bytes));var final=await p.FinalPath(a.AttachmentId);
        var outside=Path.Combine(Path.GetTempPath(),"ipqc-photo-symlink-target-"+Guid.NewGuid().ToString("N"));
        try {
            await File.WriteAllBytesAsync(outside,bytes);File.SetUnixFileMode(outside,UnixFileMode.UserRead|UnixFileMode.UserWrite);File.CreateSymbolicLink(final,outside);
            var r=await PhotoFixture.Upload(s,a.AttachmentId,bytes);Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable,r.StatusCode);
            Assert.Equal(bytes,await File.ReadAllBytesAsync(outside));Assert.Equal(0,await p.F.Read(db=>Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.AttachmentAcceptances)));
            var alias=Path.Combine(p.DirectoryPath,"alias");Directory.CreateSymbolicLink(alias,p.DirectoryPath);
            p.F.Factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["InspectionPhotos:Root"]=alias;
            Assert.Throws<PhotoStorageException>(()=>p.Fault.Inner.CheckConfigured());
            File.Delete(final);Directory.Delete(alias);
        } finally {File.Delete(outside);}
    }
    [Fact] public async Task Public_root_permissions_are_rejected() {
        await using var p=await PhotoFixture.Create();
        if(OperatingSystem.IsLinux()) {
            var saved=File.GetUnixFileMode(p.DirectoryPath);
            try {File.SetUnixFileMode(p.DirectoryPath,saved|UnixFileMode.OtherRead|UnixFileMode.OtherExecute);Assert.Throws<PhotoStorageException>(()=>p.Fault.Inner.CheckConfigured());}
            finally {File.SetUnixFileMode(p.DirectoryPath,saved);}
        } else if(OperatingSystem.IsWindows()) {
            var directory=new DirectoryInfo(p.DirectoryPath);var saved=System.IO.FileSystemAclExtensions.GetAccessControl(directory).GetSecurityDescriptorBinaryForm();
            foreach(var inheritOnly in new[]{false,true}) {
                var changed=new System.Security.AccessControl.DirectorySecurity();changed.SetSecurityDescriptorBinaryForm(saved,System.Security.AccessControl.AccessControlSections.Access);
                changed.AddAccessRule(new(new System.Security.Principal.SecurityIdentifier("S-1-1-0"),System.Security.AccessControl.FileSystemRights.ReadAndExecute,
                    System.Security.AccessControl.InheritanceFlags.ObjectInherit|System.Security.AccessControl.InheritanceFlags.ContainerInherit,
                    inheritOnly?System.Security.AccessControl.PropagationFlags.InheritOnly:System.Security.AccessControl.PropagationFlags.None,System.Security.AccessControl.AccessControlType.Allow));
                try {
                    System.IO.FileSystemAclExtensions.SetAccessControl(directory,changed);
                    if(inheritOnly) Assert.Contains(System.IO.FileSystemAclExtensions.GetAccessControl(directory).GetAccessRules(true,true,typeof(System.Security.Principal.SecurityIdentifier)).Cast<System.Security.AccessControl.FileSystemAccessRule>(),
                        r=>OperatingSystem.IsWindows() && r.IdentityReference.Value=="S-1-1-0" && (r.PropagationFlags&System.Security.AccessControl.PropagationFlags.InheritOnly)!=0);
                    Assert.Throws<PhotoStorageException>(()=>p.Fault.Inner.CheckConfigured());
                }
                finally {
                    var restored=new System.Security.AccessControl.DirectorySecurity();restored.SetSecurityDescriptorBinaryForm(saved,System.Security.AccessControl.AccessControlSections.Access);
                    System.IO.FileSystemAclExtensions.SetAccessControl(directory,restored);
                }
                p.Fault.Inner.CheckConfigured();
            }
        }
    }
    [LinuxPhotoFact] public async Task Application_root_alias_is_rejected() {
        if(!OperatingSystem.IsLinux()) throw new InvalidOperationException("Linux-only test must be skipped.");
        await using var p=await PhotoFixture.Create();var alias=Path.Combine(Path.GetTempPath(),"ipqc-photo-webroot-alias-"+Guid.NewGuid().ToString("N"));
        try {
            Directory.CreateSymbolicLink(alias,p.DirectoryPath);
            p.F.Factory.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().WebRootPath=alias;
            Assert.Throws<PhotoStorageException>(()=>p.Fault.Inner.CheckConfigured());
        } finally {Directory.Delete(alias);}
    }
}
public sealed class LinuxPhotoFactAttribute : FactAttribute {
    public LinuxPhotoFactAttribute() {if(!OperatingSystem.IsLinux()) Skip="Linux no-follow/openat symlink test; executed by Ubuntu backend CI.";}
}
