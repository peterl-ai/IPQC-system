using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace JaxPower.Ipqc.Api.Services;

public sealed class PhotoStorageException(string code) : Exception(code);
public interface IProtectedPhotoStore
{
    void CheckConfigured();
    Task Stage(string key, Stream input, long expectedLength, CancellationToken ct);
    Task<byte[]?> Read(string key, bool final, CancellationToken ct);
    void Promote(string stageKey, string finalKey);
}
public interface IPhotoDurability { void FlushExisting(string key,bool final); }

// No client paths, no delete/overwrite API. A dedicated local private volume is required.
// Pinned directory handles prevent directory replacement; Linux also uses openat/NOFOLLOW.
public sealed class ProtectedPhotoStore(IConfiguration config, IHostEnvironment environment) : IProtectedPhotoStore, IPhotoDurability
{
    public const int MaximumBytes=5*1024*1024;
    private sealed class Root : IDisposable
    {
        internal required string Path;
        internal List<SafeFileHandle> Pins=[];
        internal int Fd=>Pins[^1].DangerousGetHandle().ToInt32();
        public void Dispose() { foreach(var h in Pins.AsEnumerable().Reverse()) h.Dispose(); }
    }
    private static bool ContainsPath(string parent,string child) => child.Equals(parent,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal) ||
        child.StartsWith(parent+System.IO.Path.DirectorySeparatorChar,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);
    private Root OpenRoot()
    {
        Root? root=null;
        try {
            var raw=config["InspectionPhotos:Root"];
            if(string.IsNullOrWhiteSpace(raw) || !System.IO.Path.IsPathFullyQualified(raw) || raw.StartsWith("\\\\") || raw.Contains('\0') || (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())) throw new IOException();
            var path=System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(raw));
            var drive=System.IO.Path.GetPathRoot(path)!;
            var applicationRoots=new[]{environment.ContentRootPath,AppContext.BaseDirectory,(environment as Microsoft.AspNetCore.Hosting.IWebHostEnvironment)?.WebRootPath}
                .Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(x!)));
            if(path==System.IO.Path.TrimEndingDirectorySeparator(drive) || applicationRoots.Any(app=>ContainsPath(app,path) || ContainsPath(path,app)) || !Directory.Exists(path)) throw new IOException();
            root=new Root {Path=path};
            if(OperatingSystem.IsLinux()) {
                root.Pins.Add(LinuxHandle(open("/",0x10000|0x80000|0x20000)));
                foreach(var piece in path.Split('/',StringSplitOptions.RemoveEmptyEntries))
                    root.Pins.Add(LinuxHandle(openat(root.Fd,piece,0x10000|0x80000|0x20000,0)));
                var mode=File.GetUnixFileMode(root.Pins[^1]);
                if((mode&(UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherWrite|UnixFileMode.OtherExecute))!=0 ||
                    (mode&(UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute))!=(UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute)) throw new IOException();
            } else if(OperatingSystem.IsWindows()) {
                var current=drive;
                root.Pins.Add(WindowsDirectory(current));
                foreach(var piece in path[drive.Length..].Split(System.IO.Path.DirectorySeparatorChar,StringSplitOptions.RemoveEmptyEntries)) {
                    current=System.IO.Path.Combine(current,piece);root.Pins.Add(WindowsDirectory(current));
                }
                ValidateWindowsAcl(path);
            } else throw new IOException();
            return root;
        } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
            root?.Dispose();throw new PhotoStorageException("PhotoStorageUnavailable");
        }
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void ValidateWindowsAcl(string path)
    {
        using var current=WindowsIdentity.GetCurrent();
        var allowed=new HashSet<string>(StringComparer.OrdinalIgnoreCase) {current.User!.Value,"S-1-5-18","S-1-5-32-544"};
        foreach(var sid in config.GetSection("InspectionPhotos:AllowedStoragePrincipals").Get<string[]>() ?? []) allowed.Add(new SecurityIdentifier(sid).Value);
        var broad=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"S-1-1-0","S-1-5-11","S-1-5-32-545","S-1-5-32-546","S-1-5-7"};
        foreach(FileSystemAccessRule rule in new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access).GetAccessRules(true,true,typeof(SecurityIdentifier))) {
            if(rule.AccessControlType!=AccessControlType.Allow || (rule.PropagationFlags&PropagationFlags.InheritOnly)!=0) continue;
            var sid=rule.IdentityReference.Value;
            if(broad.Contains(sid) || !allowed.Contains(sid)) throw new IOException();
        }
    }
    private static SafeFileHandle WindowsDirectory(string path)
    {
        var h=CreateFileW(path,0x80,1,IntPtr.Zero,3,0x02000000|0x00200000,IntPtr.Zero);
        if(h.IsInvalid) {h.Dispose();throw new IOException();}
        try { if((File.GetAttributes(h)&FileAttributes.ReparsePoint)!=0) throw new IOException();return h; }
        catch {h.Dispose();throw;}
    }
    private static SafeFileHandle LinuxHandle(int fd) { if(fd<0) throw new IOException();return new(new IntPtr(fd),true); }
    private static string Leaf(string key,bool final)
    {
        if(key.Length!=32 || key.Any(c=>!(c is >= '0' and <= '9' or >= 'a' and <= 'f'))) throw new PhotoStorageException("InvalidPhotoStorageIdentity");
        return key+(final?".blob":".stage");
    }
    private static SafeFileHandle Open(Root root,string leaf,bool create)
    {
        SafeFileHandle h;
        if(OperatingSystem.IsLinux()) {
            var fd=openat(root.Fd,leaf,0x80000|0x20000|0x800|(create?2|0x40|0x80:0),0x180);
            if(fd<0) { if(!create && Marshal.GetLastPInvokeError()==2) throw new FileNotFoundException();throw new IOException(); }
            h=new(new IntPtr(fd),true);
            if(flock(fd,(create?2:1)|4)!=0) {h.Dispose();throw new IOException();}
        } else {
            h=CreateFileW(System.IO.Path.Combine(root.Path,leaf),create?0xC0010000:0x80000000,1,IntPtr.Zero,create?1u:3u,0x00200000|(create?0x80000000u:0),IntPtr.Zero);
            if(h.IsInvalid) {var error=Marshal.GetLastPInvokeError();h.Dispose();if(!create && error is 2 or 3) throw new FileNotFoundException();throw new IOException();}
        }
        try {
            if((File.GetAttributes(h)&(FileAttributes.ReparsePoint|FileAttributes.Directory))!=0 || (OperatingSystem.IsWindows() && GetFileType(h)!=1)) throw new IOException();
            _=RandomAccess.GetLength(h);return h;
        } catch {h.Dispose();throw;}
    }
    public void CheckConfigured() {using var root=OpenRoot();}
    public void FlushExisting(string key,bool final)
    {
        try {
            using var root=OpenRoot();var leaf=Leaf(key,final);
            if(OperatingSystem.IsLinux()) {
                using var h=Open(root,leaf,false);
                if(fsync(h.DangerousGetHandle().ToInt32())!=0 || fsync(root.Fd)!=0) throw new IOException();
            } else {
                using var h=CreateFileW(System.IO.Path.Combine(root.Path,leaf),0xC0000000,1,IntPtr.Zero,3,0x00200000,IntPtr.Zero);
                if(h.IsInvalid || (File.GetAttributes(h)&(FileAttributes.ReparsePoint|FileAttributes.Directory))!=0 || !FlushFileBuffers(h)) throw new IOException();
            }
        } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) {throw new PhotoStorageException("PhotoStorageFlushFailed");}
    }
    public async Task Stage(string key,Stream input,long expectedLength,CancellationToken ct)
    {
        try {
            using var root=OpenRoot();using var handle=Open(root,Leaf(key,false),true);
            using var file=new FileStream(handle,FileAccess.ReadWrite,65536,false);
            var buffer=new byte[65536];long total=0;int read;
            while((read=await input.ReadAsync(buffer,ct))!=0) {
                total+=read;if(total>MaximumBytes || total>expectedLength) throw new PhotoStorageException("PhotoSizeMismatch");
                await file.WriteAsync(buffer.AsMemory(0,read),ct);
            }
            if(total!=expectedLength) throw new PhotoStorageException("PhotoSizeMismatch");
            await file.FlushAsync(ct);file.Flush(true);
            if(OperatingSystem.IsLinux() && fsync(root.Fd)!=0) throw new IOException();
        } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { throw new PhotoStorageException("PhotoStorageWriteFailed"); }
    }
    public async Task<byte[]?> Read(string key,bool final,CancellationToken ct)
    {
        try {
            using var root=OpenRoot();using var h=Open(root,Leaf(key,final),false);
            using var file=new FileStream(h,FileAccess.Read,65536,false);
            if(file.Length is <1 or >MaximumBytes) throw new PhotoStorageException("PhotoBytesCorrupt");
            var bytes=new byte[(int)file.Length];await file.ReadExactlyAsync(bytes,ct);
            if(file.ReadByte()!=-1) throw new PhotoStorageException("PhotoBytesCorrupt");
            return bytes;
        } catch(FileNotFoundException) {return null;}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) {throw new PhotoStorageException("PhotoStorageReadFailed");}
    }
    public void Promote(string stageKey,string finalKey)
    {
        try {
            using var root=OpenRoot();var from=Leaf(stageKey,false);var to=Leaf(finalKey,true);
            if(OperatingSystem.IsLinux()) {
                if(renameat2(root.Fd,from,root.Fd,to,1)!=0) throw new IOException();
                if(fsync(root.Fd)!=0) throw new IOException();
            } else {
                // The verified source handle, rather than an independently resolved source path.
                using var h=CreateFileW(System.IO.Path.Combine(root.Path,from),0x80010000,1,IntPtr.Zero,3,0x00200000,IntPtr.Zero);
                if(h.IsInvalid || (File.GetAttributes(h)&(FileAttributes.ReparsePoint|FileAttributes.Directory))!=0) throw new IOException();
                var name=System.Text.Encoding.Unicode.GetBytes(System.IO.Path.Combine(root.Path,to));
                // Win32 FILE_RENAME_INFO.FileName must also be NUL terminated. The byte
                // length excludes that terminator; omitting it can append heap garbage.
                var offset=IntPtr.Size==8?20:12;var buffer=Marshal.AllocHGlobal(offset+name.Length+2);
                try {
                    Marshal.Copy(new byte[offset+name.Length+2],0,buffer,offset+name.Length+2);
                    Marshal.WriteInt32(buffer,IntPtr.Size==8?16:8,name.Length);Marshal.Copy(name,0,buffer+offset,name.Length);
                    if(!SetFileInformationByHandle(h,3,buffer,(uint)(offset+name.Length+2))) throw new IOException();
                } finally {Marshal.FreeHGlobal(buffer);}
            }
        } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) {throw new PhotoStorageException("PhotoStoragePromoteFailed");}
    }
    [DllImport("libc",SetLastError=true)] private static extern int open(string path,int flags);
    [DllImport("libc",SetLastError=true)] private static extern int openat(int dir,string path,int flags,int mode);
    [DllImport("libc",SetLastError=true)] private static extern int renameat2(int olddir,string oldname,int newdir,string newname,uint flags);
    [DllImport("libc",SetLastError=true)] private static extern int fsync(int fd);
    [DllImport("libc",SetLastError=true)] private static extern int flock(int fd,int operation);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFileW(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern uint GetFileType(SafeFileHandle handle);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool FlushFileBuffers(SafeFileHandle handle);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetFileInformationByHandle(SafeFileHandle handle,int info,IntPtr buffer,uint size);
}
