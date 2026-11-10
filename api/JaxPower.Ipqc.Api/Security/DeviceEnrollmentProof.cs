using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using JaxPower.Ipqc.Api.Services;

namespace JaxPower.Ipqc.Api.Security;

public sealed class DeviceEnrollmentOptions
{
    public bool CleanupEnabled { get; set; }
    public bool BootstrapEnabled { get; set; }
    public string EnvironmentId { get; set; } = "";
    public bool DevelopmentIssuerEnabled { get; set; }
    public string RootPfxPath { get; set; } = "";
    public string RootPassword { get; set; } = "";
    public string ReceiptDirectory { get; set; } = "";
    public bool WorkerEnabled { get; set; } = true;

    public void Validate(IHostEnvironment host)
    {
        // Obsolete enable flags cannot reopen enrollment or load unused issuer material.
        if (CleanupEnabled && (string.IsNullOrWhiteSpace(EnvironmentId) || EnvironmentId.Length > 200))
            throw new InvalidOperationException("Configure the original Enrollment:EnvironmentId before enabling cleanup.");
    }
}

public static class DeviceProvisioningRetirement
{
    public const string Code = "DeviceProvisioningRetired";
    public static ProvisioningException Rejected() => new(Code, 410);
    public static IResult Response() => Results.Problem(statusCode: 410, title: Code,
        extensions: new Dictionary<string, object?> { ["code"] = Code });
}

public static class DeviceEnrollmentProof
{
    // RFC 7515 / RFC 7518 ES256: exactly 64 unsigned big-endian octets R || S,
    // each left-padded to 32 bytes. ASN.1 DER signatures from Java Signature are NOT JWS wire format.
    public static readonly string ProtectedHeader = Encode(Encoding.UTF8.GetBytes("{\"alg\":\"ES256\",\"typ\":\"ipqc-enrollment+jws\"}"));
    public static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] Decode(string value)
    {
        if (value.Length == 0 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) throw Invalid();
        try
        {
            var bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
            if (Encode(bytes) != value) throw Invalid();
            return bytes;
        }
        catch (FormatException) { throw Invalid(); }
    }
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));
    public static bool MatchesHash(string value, string hash) => hash.Length == 64 &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(value)), Encoding.ASCII.GetBytes(hash));
    public static (string Csr, string CsrHash, string Spki, string SpkiHash) ReadCsr(string value, Guid device)
    {
        if (value is null || value.Length > 22000) throw Invalid();
        try
        {
            var bytes = Convert.FromBase64String(value);
            if (bytes.Length > 16384) throw Invalid();
            var csr = CertificateRequest.LoadSigningRequest(bytes, HashAlgorithmName.SHA256);
            using var key = csr.PublicKey.GetECDsaPublicKey();
            if (key is null || key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7" ||
                csr.SubjectName.Name != $"CN=IPQC-{device:D}") throw Invalid();
            var spki = key.ExportSubjectPublicKeyInfo();
            return (Convert.ToBase64String(bytes), Hash(bytes), Convert.ToBase64String(spki), Hash(spki));
        }
        catch (Exception e) when (e is CryptographicException or FormatException or ArgumentException) { throw Invalid(); }
    }
    public static byte[] Verify(string jws, string spkiBase64, string expectedPayloadHash)
    {
        if (jws is null || jws.Length > 24000) throw Invalid();
        var parts = jws.Split('.');
        if (parts.Length != 3 || parts[0] != ProtectedHeader) throw Invalid();
        var payload = Decode(parts[1]); var signature = Decode(parts[2]);
        if (signature.Length != 64 || Hash(payload) != expectedPayloadHash) throw Invalid();
        try
        {
            using var key = ECDsa.Create(); var spki = Convert.FromBase64String(spkiBase64);
            key.ImportSubjectPublicKeyInfo(spki, out var read);
            if (read != spki.Length || key.KeySize != 256 ||
                !key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) throw Invalid();
            return payload;
        }
        catch (Exception e) when (e is CryptographicException or FormatException or ArgumentException) { throw Invalid(); }
    }
    private static ProvisioningException Invalid() => new("InvalidProof", 400);
}
