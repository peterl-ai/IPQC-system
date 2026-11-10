using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace JaxPower.Ipqc.Api.Security;

public sealed record DeviceIssuerProfile(string Id, string RootCertificatePem, DateTime NotAfterUtc);
public sealed record DeviceIssuanceRequest(Guid OperationId, Guid DeviceId, Guid CredentialId, string SpkiBase64,
    string RootCertificatePem, DateTime NotBeforeUtc, DateTime NotAfterUtc);
public enum IssuerOutcome { NotIssued, Unknown, Issued }
public sealed record DeviceIssuanceResult(IssuerOutcome Outcome, string? CertificatePem = null);

// A production IT adapter must recover the ORIGINAL result for the same operation ID.
// NotIssued means definitively not published/in flight; uncertainty must return Unknown.
public interface IDeviceCertificateIssuer
{
    bool Available { get; }
    DeviceIssuerProfile Profile { get; }
    Task<DeviceIssuanceResult> RecoverAsync(Guid operation, CancellationToken ct);
    Task<DeviceIssuanceResult> IssueAsync(DeviceIssuanceRequest request, CancellationToken ct);
    Task RevokeAsync(Guid operation, CancellationToken ct);
}
public sealed class UnconfiguredDeviceCertificateIssuer : IDeviceCertificateIssuer
{
    public bool Available => false;
    public DeviceIssuerProfile Profile => throw new InvalidOperationException("PKI is not configured.");
    public Task<DeviceIssuanceResult> RecoverAsync(Guid operation, CancellationToken ct) => Task.FromResult(new DeviceIssuanceResult(IssuerOutcome.Unknown));
    public Task<DeviceIssuanceResult> IssueAsync(DeviceIssuanceRequest request, CancellationToken ct) => throw new InvalidOperationException("PKI is not configured.");
    public Task RevokeAsync(Guid operation, CancellationToken ct) => throw new InvalidOperationException("PKI is not configured.");
}

// The application DI surface never exposes signing, including isolated cleanup configurations.
public sealed class CleanupOnlyDeviceCertificateIssuer(IDeviceCertificateIssuer inner) : IDeviceCertificateIssuer, IDisposable
{
    public bool Available => inner.Available;
    public DeviceIssuerProfile Profile => inner.Profile;
    public Task<DeviceIssuanceResult> RecoverAsync(Guid operation, CancellationToken ct) => inner.RecoverAsync(operation, ct);
    public Task RevokeAsync(Guid operation, CancellationToken ct) => inner.RevokeAsync(operation, ct);
    public Task<DeviceIssuanceResult> IssueAsync(DeviceIssuanceRequest request, CancellationToken ct) =>
        throw DeviceProvisioningRetirement.Rejected();
    public void Dispose() => (inner as IDisposable)?.Dispose();
}

public static class DeviceCertificateProfile
{
    public static CertificateRequest Request(DeviceIssuanceRequest input, ECDsa publicKey)
    {
        var request = new CertificateRequest($"CN=IPQC-{input.DeviceId:D}", publicKey, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.2") }, true));
        var san = new SubjectAlternativeNameBuilder(); san.AddUri(new Uri($"urn:ipqc:device:{input.DeviceId:D}:credential:{input.CredentialId:D}"));
        request.CertificateExtensions.Add(san.Build()); return request;
    }
    public static string Validate(DeviceIssuanceRequest input, string pem, DateTime now)
    {
        if (pem.Length > 32000) throw new CryptographicException("Invalid issuer result.");
        using var leaf = X509Certificate2.CreateFromPem(pem); using var root = X509Certificate2.CreateFromPem(input.RootCertificatePem);
        using var key = leaf.GetECDsaPublicKey();
        if (key is null || Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) != input.SpkiBase64 ||
            leaf.SubjectName.Name != $"CN=IPQC-{input.DeviceId:D}" || leaf.NotBefore.ToUniversalTime() > now || leaf.NotAfter.ToUniversalTime() <= now ||
            leaf.NotBefore.ToUniversalTime() != Second(input.NotBeforeUtc) || leaf.NotAfter.ToUniversalTime() != Second(input.NotAfterUtc))
            throw new CryptographicException("Invalid issuer result.");
        var expected = Request(input, key);
        foreach (var extension in expected.CertificateExtensions)
        {
            var actual = leaf.Extensions.SingleOrDefault(x => x.Oid?.Value == extension.Oid?.Value);
            if (actual is null || actual.Critical != extension.Critical || !actual.RawData.SequenceEqual(extension.RawData))
                throw new CryptographicException("Invalid issuer result.");
        }
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust; chain.ChainPolicy.CustomTrustStore.Add(root);
        // P1-03A only installs an explicit isolated dev/test issuer. This is not Native TLS validation.
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; chain.ChainPolicy.VerificationTime = now;
        if (!chain.Build(leaf)) throw new CryptographicException("Invalid issuer chain.");
        return leaf.GetCertHashString(HashAlgorithmName.SHA256);
    }
    private static DateTime Second(DateTime value) => new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
}

// Explicit dev/test only. Keys are provisioned OUTSIDE the repository; never generated at startup.
// Public issuance receipts are durable before returning. An OS file lock serializes each operation,
// including across processes; an interrupted, unpublished signature is not an issuance result.
public sealed class DevelopmentDeviceCertificateIssuer : IDeviceCertificateIssuer, IDisposable
{
    private readonly X509Certificate2 root;
    private readonly string directory;
    private readonly bool cleanupOnly;
    public bool Available => true;
    public DeviceIssuerProfile Profile { get; }
    public DevelopmentDeviceCertificateIssuer(DeviceEnrollmentOptions options, IHostEnvironment host, bool cleanupOnly = false)
    {
        this.cleanupOnly = cleanupOnly;
        options.Validate(host);
        if (!options.DevelopmentIssuerEnabled || !(host.IsDevelopment() || host.IsEnvironment("Testing"))) throw new InvalidOperationException("Development PKI is disabled.");
        var repo = new DirectoryInfo(host.ContentRootPath);
        while (repo.Parent is not null && !Directory.Exists(Path.Combine(repo.FullName, ".git")) && !File.Exists(Path.Combine(repo.FullName, ".git"))) repo = repo.Parent;
        if (repo.Parent is null) repo = new DirectoryInfo(host.ContentRootPath);
        string Outside(string value)
        {
            if (!Path.IsPathFullyQualified(value)) throw new InvalidOperationException("Development PKI paths must be absolute and outside the repository.");
            var full = Path.GetFullPath(value);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (full.TrimEnd(Path.DirectorySeparatorChar).Equals(repo.FullName.TrimEnd(Path.DirectorySeparatorChar), comparison) ||
                full.StartsWith(repo.FullName.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
                throw new InvalidOperationException("Development PKI must not be stored in the repository.");
            return full;
        }
        directory = Outside(options.ReceiptDirectory);
        if (cleanupOnly && !Directory.Exists(directory))
            throw new IOException("The original issuer journal is unavailable.");
        root = X509CertificateLoader.LoadPkcs12FromFile(Outside(options.RootPfxPath), options.RootPassword, X509KeyStorageFlags.EphemeralKeySet);
        if (!root.HasPrivateKey || root.Extensions.OfType<X509BasicConstraintsExtension>().SingleOrDefault()?.CertificateAuthority != true)
            throw new InvalidOperationException("Configure a dedicated development issuing CA.");
        Directory.CreateDirectory(directory);
        Profile = new(root.GetCertHashString(HashAlgorithmName.SHA256), root.ExportCertificatePem(), root.NotAfter.ToUniversalTime());
    }
    private string PathFor(Guid id, string suffix) => Path.Combine(directory, id.ToString("N") + suffix);
    private FileStream Lock(Guid id) => new(PathFor(id, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    public Task<DeviceIssuanceResult> RecoverAsync(Guid operation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            using var lease = Lock(operation); var original = Read(operation);
            if (cleanupOnly && original.Outcome == IssuerOutcome.NotIssued && !File.Exists(PathFor(operation, ".revoked")))
                original = new(IssuerOutcome.Unknown); // Missing receipts are not definitive non-issuance during retirement.
            return Task.FromResult(original);
        }
        catch (IOException) { return Task.FromResult(new DeviceIssuanceResult(IssuerOutcome.Unknown)); }
    }
    private DeviceIssuanceResult Read(Guid id) => File.Exists(PathFor(id, ".json"))
        ? new(IssuerOutcome.Issued, JsonSerializer.Deserialize<PublicReceipt>(File.ReadAllText(PathFor(id, ".json")))!.CertificatePem)
        : new(IssuerOutcome.NotIssued);
    public Task<DeviceIssuanceResult> IssueAsync(DeviceIssuanceRequest request, CancellationToken ct)
    {
        if (cleanupOnly) throw DeviceProvisioningRetirement.Rejected();
        ct.ThrowIfCancellationRequested();
        try
        {
            using var lease = Lock(request.OperationId);
            var contentHash = DeviceEnrollmentProof.Hash(JsonSerializer.Serialize(request));
            if (File.Exists(PathFor(request.OperationId, ".json")))
            {
                var old = JsonSerializer.Deserialize<PublicReceipt>(File.ReadAllText(PathFor(request.OperationId, ".json")))!;
                if (old.ContentHash != contentHash) throw new CryptographicException("Issuance operation mismatch.");
                return Task.FromResult(new DeviceIssuanceResult(IssuerOutcome.Issued, old.CertificatePem));
            }
            if (request.RootCertificatePem != Profile.RootCertificatePem || File.Exists(PathFor(request.OperationId, ".revoked")))
                throw new CryptographicException("Issuer operation is no longer issuable.");
            using var key = ECDsa.Create(); key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(request.SpkiBase64), out _);
            var serial = SHA256.HashData(request.OperationId.ToByteArray())[..16]; serial[0] &= 0x7f; serial[^1] |= 1;
            using var rsa = root.GetRSAPrivateKey(); using var ec = root.GetECDsaPrivateKey();
            var generator = rsa is not null ? X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1) :
                ec is not null ? X509SignatureGenerator.CreateForECDsa(ec) : throw new CryptographicException("Unsupported development issuer.");
            using var certificate = DeviceCertificateProfile.Request(request, key).Create(root.SubjectName, generator,
                new DateTimeOffset(DateTime.SpecifyKind(request.NotBeforeUtc, DateTimeKind.Utc)),
                new DateTimeOffset(DateTime.SpecifyKind(request.NotAfterUtc, DateTimeKind.Utc)), serial);
            var pem = certificate.ExportCertificatePem();
            DurableWrite(PathFor(request.OperationId, ".json"), JsonSerializer.Serialize(new PublicReceipt(contentHash, pem)));
            return Task.FromResult(new DeviceIssuanceResult(IssuerOutcome.Issued, pem));
        }
        catch (IOException) { return Task.FromResult(new DeviceIssuanceResult(IssuerOutcome.Unknown)); }
    }
    public Task RevokeAsync(Guid operation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); using var lease = Lock(operation);
        if (cleanupOnly && !File.Exists(PathFor(operation, ".json")) && !File.Exists(PathFor(operation, ".revoked")))
            throw new IOException("The original issuance result is unresolved.");
        if (!File.Exists(PathFor(operation, ".revoked"))) DurableWrite(PathFor(operation, ".revoked"), "Revoked");
        return Task.CompletedTask;
    }
    private static void DurableWrite(string target, string value)
    {
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { var bytes = System.Text.Encoding.UTF8.GetBytes(value); file.Write(bytes); file.Flush(true); }
            File.Move(temporary, target, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private sealed record PublicReceipt(string ContentHash, string CertificatePem);
    public void Dispose() => root.Dispose();
}
