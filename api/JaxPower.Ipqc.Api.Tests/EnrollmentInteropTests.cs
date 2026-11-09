using System.Diagnostics;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Text;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class EnrollmentInteropTests
{
    [Fact]
    public void Development_issuer_refuses_repository_root_or_child_receipt_and_private_key_paths()
    {
        var repo=new DirectoryInfo(Directory.GetCurrentDirectory());
        while(repo.Parent is not null&&!Directory.Exists(Path.Combine(repo.FullName,".git"))&&!File.Exists(Path.Combine(repo.FullName,".git")))repo=repo.Parent;
        var host=new EnrollmentTestHost{ContentRootPath=repo.FullName};
        foreach(var path in new[]{repo.FullName,Path.Combine(repo.FullName,"uncommitted-pki")})
        {
            var options=new DeviceEnrollmentOptions{DevelopmentIssuerEnabled=true,EnvironmentId="test",ReceiptDirectory=path,RootPfxPath=Path.Combine(Path.GetTempPath(),"nonexistent-pki.pfx")};
            Assert.Throws<InvalidOperationException>(()=>new DevelopmentDeviceCertificateIssuer(options,host));
        }
    }
    // OpenSSL and Android SHA256withECDSA use ASN.1 DER. This is a software format
    // interop gate, not evidence of Android Keystore execution or non-exportability.
    [Fact]
    public async Task OpenSSL_DER_signature_converted_to_JOSE_verifies_in_ASPNET_and_reverse_verifies_in_OpenSSL()
    {
        var executable=OperatingSystem.IsWindows()?@"C:\Program Files\Git\usr\bin\openssl.exe":"openssl";
        var directory=Path.Combine(Path.GetTempPath(),"ipqc-wire-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
            await File.WriteAllTextAsync(Path.Combine(directory,"key.pem"),key.ExportPkcs8PrivateKeyPem());
            await File.WriteAllTextAsync(Path.Combine(directory,"public.pem"),key.ExportSubjectPublicKeyInfoPem());
            var payload=Encoding.UTF8.GetBytes("{\"test\":\"isolated-wire-interop\"}");
            var prefix=DeviceEnrollmentProof.ProtectedHeader+"."+DeviceEnrollmentProof.Encode(payload);
            await File.WriteAllBytesAsync(Path.Combine(directory,"input"),Encoding.ASCII.GetBytes(prefix));
            await OpenSsl("dgst","-sha256","-sign","key.pem","-out","signature.der","input");
            var der=await File.ReadAllBytesAsync(Path.Combine(directory,"signature.der"));var raw=DerToJose(der);
            Assert.Equal(64,raw.Length);
            Assert.Equal(payload,DeviceEnrollmentProof.Verify(prefix+"."+DeviceEnrollmentProof.Encode(raw),Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),DeviceEnrollmentProof.Hash(payload)));
            Assert.Throws<ProvisioningException>(()=>DeviceEnrollmentProof.Verify(prefix+"."+DeviceEnrollmentProof.Encode(der),Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),DeviceEnrollmentProof.Hash(payload)));
            var netRaw=key.SignData(Encoding.ASCII.GetBytes(prefix),HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            await File.WriteAllBytesAsync(Path.Combine(directory,"net.der"),JoseToDer(netRaw));
            Assert.Contains("Verified OK",await OpenSsl("dgst","-sha256","-verify","public.pem","-signature","net.der","input"));
            async Task<string> OpenSsl(params string[] args)
            {
                var info=new ProcessStartInfo(executable){WorkingDirectory=directory,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
                foreach(var arg in args)info.ArgumentList.Add(arg);
                using var process=Process.Start(info)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
                Assert.True(process.ExitCode==0,await error);return await output;
            }
        }
        finally{Directory.Delete(directory,true);}
    }

    [Fact]
    public void DER_conversion_preserves_leading_zeros_and_rejects_negative_oversize_trailing_and_zero_values()
    {
        var raw=new byte[64];raw[0]=0x80;raw[63]=1;
        Assert.Equal(raw,DerToJose(JoseToDer(raw)));
        Assert.Throws<AsnContentException>(()=>DerToJose([0x30,0x06,0x02,0x01,0xFF,0x02,0x01,1]));
        Assert.Throws<AsnContentException>(()=>DerToJose([0x30,0x06,0x02,0x01,0,0x02,0x01,1]));
        Assert.Throws<AsnContentException>(()=>DerToJose(JoseToDer(raw).Concat(new byte[]{0}).ToArray()));
        var writer=new AsnWriter(AsnEncodingRules.DER);writer.PushSequence();writer.WriteIntegerUnsigned(Enumerable.Repeat((byte)1,33).ToArray());writer.WriteInteger(1);writer.PopSequence();
        Assert.Throws<AsnContentException>(()=>DerToJose(writer.Encode()));
    }

    private static byte[] DerToJose(byte[] der)
    {
        var reader=new AsnReader(der,AsnEncodingRules.DER);var sequence=reader.ReadSequence();var result=new byte[64];
        for(var i=0;i<2;i++)
        {
            var value=sequence.ReadIntegerBytes().Span;
            if((value[0]&0x80)!=0)throw new AsnContentException();
            if(value[0]==0)value=value[1..];
            if(value.Length is 0 or >32 || !value.ContainsAnyExcept((byte)0))throw new AsnContentException();
            value.CopyTo(result.AsSpan(i*32+32-value.Length));
        }
        sequence.ThrowIfNotEmpty();reader.ThrowIfNotEmpty();return result;
    }
    private static byte[] JoseToDer(byte[] raw)
    {
        Assert.Equal(64,raw.Length);var writer=new AsnWriter(AsnEncodingRules.DER);writer.PushSequence();
        for(var i=0;i<2;i++){var part=raw.AsSpan(i*32,32);while(part.Length>1&&part[0]==0)part=part[1..];writer.WriteIntegerUnsigned(part);}
        writer.PopSequence();return writer.Encode();
    }
}
