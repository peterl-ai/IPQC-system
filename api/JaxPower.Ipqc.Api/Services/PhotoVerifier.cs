using System.Buffers.Binary;
using System.Security.Cryptography;
using SkiaSharp;

namespace JaxPower.Ipqc.Api.Services;

public sealed record VerifiedPhoto(long ByteCount,string Sha256,string ContentType,string Encoding,int Width,int Height);
public sealed class PhotoVerifier
{
    private readonly SemaphoreSlim decoders=new(2);
    public async Task<VerifiedPhoto> Verify(byte[] bytes,long size,string hash,string contentType,CancellationToken ct)
    {
        if(bytes.Length!=size || bytes.Length is <1 or >ProtectedPhotoStore.MaximumBytes || Convert.ToHexString(SHA256.HashData(bytes))!=hash)
            throw new PhotoStorageException("PhotoContentMismatch");
        await decoders.WaitAsync(ct);
        try {
            using var data=SKData.CreateCopy(bytes);using var codec=SKCodec.Create(data);
            if(codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png) || codec.FrameCount>1 ||
                codec.Info.Width is <1 or >6000 || codec.Info.Height is <1 or >6000 || (long)codec.Info.Width*codec.Info.Height>12000000)
                throw new PhotoStorageException("UnsupportedOrCorruptPhoto");
            var png=codec.EncodedFormat==SKEncodedImageFormat.Png;
            var mime=png?"image/png":"image/jpeg";
            if(mime!=contentType) throw new PhotoStorageException("PhotoTypeMismatch");
            if(png) ValidatePng(bytes);
            else if(bytes.Length<4 || bytes[0]!=255 || bytes[1]!=216 || bytes[^2]!=255 || bytes[^1]!=217) throw new PhotoStorageException("UnsupportedOrCorruptPhoto");
            var info=new SKImageInfo(codec.Info.Width,codec.Info.Height,SKColorType.Rgba8888,SKAlphaType.Premul);
            using var bitmap=new SKBitmap(info);
            if(codec.GetPixels(info,bitmap.GetPixels())!=SKCodecResult.Success) throw new PhotoStorageException("UnsupportedOrCorruptPhoto");
            return new(bytes.LongLength,hash,mime,png?"PNG":"JPEG",info.Width,info.Height);
        } finally {decoders.Release();}
    }
    private static void ValidatePng(byte[] b)
    {
        if(b.Length<45 || !b.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})) throw new PhotoStorageException("UnsupportedOrCorruptPhoto");
        var p=8;var idat=false;var first=true;
        while(p<=b.Length-12) {
            var length=BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(p,4));if(length>(uint)(b.Length-p-12)) break;
            var n=(int)length;var type=System.Text.Encoding.ASCII.GetString(b,p+4,4);
            if(first && (type!="IHDR" || n!=13) || type=="acTL") break;first=false;
            uint crc=0xffffffff;foreach(var v in b.AsSpan(p+4,n+4)) {crc^=v;for(var k=0;k<8;k++) crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);}
            if(~crc!=BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(p+8+n,4))) break;
            p+=n+12;if(type=="IDAT") idat=true;
            if(type=="IEND") {if(n==0 && idat && p==b.Length) return;break;}
        }
        throw new PhotoStorageException("UnsupportedOrCorruptPhoto");
    }
}
