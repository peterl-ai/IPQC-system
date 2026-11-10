using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Domain;
using JaxPower.Ipqc.Api.Security;
using JaxPower.Ipqc.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

internal sealed class EnrollmentClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan delta) => now += delta;
}
internal sealed class EnrollmentTestHost : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Testing";
    public string ApplicationName { get; set; } = "EnrollmentFixture";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
internal sealed class ControlledIssuer(DevelopmentDeviceCertificateIssuer inner) : IDeviceCertificateIssuer
{
    public bool Available { get; set; } = true;
    public bool LoseResponseOnce { get; set; }
    public bool Unknown { get; set; }
    public IssuerOutcome? ForcedOutcome { get; set; }
    public Func<Task>? BeforeRecover { get; set; }
    public bool FailRevocation { get; set; }
    public HashSet<Guid> FailedOperations { get; } = [];
    public List<Guid> Revocations { get; } = [];
    public List<Guid> RevocationAcknowledgements { get; } = [];
    public Func<Task>? BeforeRevoke { get; set; }
    public int Issues { get; private set; }
    public string? Failure { get; private set; }
    public Func<Task>? BeforeIssue { get; set; }
    public Func<DeviceIssuanceRequest, DeviceIssuanceResult, DeviceIssuanceResult>? Transform { get; set; }
    private readonly AsyncLocal<Action?> outside = new();
    public Action? OutsideTransaction { get => outside.Value; set => outside.Value = value; }
    public DeviceIssuerProfile Profile => inner.Profile;
    public async Task<DeviceIssuanceResult> RecoverAsync(Guid operation, CancellationToken ct)
    {
        OutsideTransaction?.Invoke(); if (BeforeRecover is not null) await BeforeRecover();
        if (Unknown || ForcedOutcome is {}) return new(Unknown ? IssuerOutcome.Unknown : ForcedOutcome!.Value);
        return await inner.RecoverAsync(operation, ct);
    }
    public async Task<DeviceIssuanceResult> IssueAsync(DeviceIssuanceRequest request, CancellationToken ct)
    {
        OutsideTransaction?.Invoke(); if (BeforeIssue is not null) await BeforeIssue();
        DeviceIssuanceResult result;
        try { result = await inner.IssueAsync(request, ct); Issues++; }
        catch(Exception ex) { Failure = ex.ToString(); throw; }
        if (LoseResponseOnce) { LoseResponseOnce = false; throw new IOException("Injected lost signer response."); }
        return Transform?.Invoke(request, result) ?? result;
    }
    public async Task RevokeAsync(Guid operation, CancellationToken ct)
    {
        OutsideTransaction?.Invoke(); Revocations.Add(operation);
        if (BeforeRevoke is not null) await BeforeRevoke();
        if (FailRevocation || FailedOperations.Contains(operation)) throw new IOException("Injected unresolved signer revocation.");
        await inner.RevokeAsync(operation, ct);
        RevocationAcknowledgements.Add(operation);
    }
}
internal sealed record HistoricalEnrollment(Guid Id, Guid DeviceId, Guid? CredentialId, Guid? OperationId);
internal sealed class EnrollmentFixture : IAsyncDisposable
{
    public required ProvisioningFixture App { get; init; }
    public required HttpClient Bootstrap { get; init; }
    public required string DirectoryPath { get; init; }
    public required DeviceEnrollmentOptions Options { get; init; }
    public required DevelopmentDeviceCertificateIssuer DevelopmentIssuer { get; init; }
    public required ControlledIssuer Issuer { get; init; }
    public required EnrollmentClock Clock { get; init; }
    public static async Task<EnrollmentFixture> CreateAsync(SaveChangesInterceptor? failure = null)
    {
        var folder = Path.Combine(Path.GetTempPath(), "ipqc-enrollment-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var clock = new EnrollmentClock(); var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var options = new DeviceEnrollmentOptions { CleanupEnabled = true, EnvironmentId = "isolated-ipqc-test",
            DevelopmentIssuerEnabled = true, RootPfxPath = Path.Combine(folder, "root.pfx"), RootPassword = password,
            ReceiptDirectory = Path.Combine(folder, "public-receipts"), WorkerEnabled = false };
        using (var rootKey = RSA.Create(2048))
        {
            var request = new CertificateRequest("CN=IPQC Synthetic Historical CA", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            using var root = request.CreateSelfSigned(clock.GetUtcNow().AddDays(-2), clock.GetUtcNow().AddYears(1));
            await File.WriteAllBytesAsync(options.RootPfxPath, root.Export(X509ContentType.Pfx, password));
        }
        // A test-only historical signer creates old public receipts, never an application API/DI enable seam.
        var development = new DevelopmentDeviceCertificateIssuer(options, new EnrollmentTestHost()); var issuer = new ControlledIssuer(development);
        var app = await ProvisioningFixture.CreateAsync(failure, services =>
        {
            services.RemoveAll<DeviceEnrollmentOptions>(); services.AddSingleton(options);
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
            services.RemoveAll<IDeviceCertificateIssuer>(); services.AddSingleton<IDeviceCertificateIssuer>(new CleanupOnlyDeviceCertificateIssuer(issuer));
        });
        return new() { App = app, Bootstrap = app.Factory.CreateClient(), DirectoryPath = folder, Options = options,
            DevelopmentIssuer = development, Issuer = issuer, Clock = clock };
    }
    public async Task<DeviceOutput> BoundDevice(string code = "A") => await App.DeviceAsync("Synthetic historical PAD", (await App.LineAsync(code)).Id);
    public async Task<HistoricalEnrollment> Seed(DeviceOutput d, EnrollmentState state = EnrollmentState.Approved,
        CandidateCredentialState? credentialState = CandidateCredentialState.PendingIssuance, bool pending = false, bool receipt = false)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = Clock.GetUtcNow().UtcDateTime;
        var csr = Csr(key,d.Id); var spki = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        var e = new DeviceEnrollment { Id=Guid.NewGuid(), DeviceId=d.Id, LineId=d.LineId!.Value, Environment=Options.EnvironmentId,
            SourceBindingVersion=d.BindingVersion, TargetBindingVersion=d.BindingVersion+1, State=state,
            CreatedByUserId=TestAccounts.AdminId, CreatedAtUtc=now.AddMinutes(-5), InvitationExpiresAtUtc=now.AddMinutes(5), ExpiresAtUtc=now.AddMinutes(25),
            InvitationHash=DeviceEnrollmentProof.Hash("synthetic-historical-invitation"), CsrDerBase64=csr, CsrSha256=DeviceEnrollmentProof.Hash(Convert.FromBase64String(csr)),
            SpkiBase64=spki, SpkiSha256=DeviceEnrollmentProof.Hash(key.ExportSubjectPublicKeyInfo()),
            InvitationConsumedAtUtc=now.AddMinutes(-4), ProofVerifiedAtUtc=now.AddMinutes(-4), ApprovedByUserId=TestAccounts.AdminId,
            ApprovalOperationId=Guid.NewGuid(), ApprovalContentHash=DeviceEnrollmentProof.Hash("historical-approval"), ApprovedAtUtc=now.AddMinutes(-3),
            TerminalReason=(int)state>6?"Historical closure":null };
        DeviceCredential? k = credentialState is null ? null : new() { Id=Guid.NewGuid(),EnrollmentId=e.Id,DeviceId=d.Id,LineId=e.LineId,
            BindingVersion=e.TargetBindingVersion,IssuanceOperationId=Guid.NewGuid(),State=credentialState.Value,ExternalRevocationPending=pending,
            SpkiBase64=spki,IssuerId=DevelopmentIssuer.Profile.Id,RootCertificatePem=DevelopmentIssuer.Profile.RootCertificatePem,
            NotBeforeUtc=now.AddMinutes(-2),NotAfterUtc=now.AddDays(30) };
        if (k is not null && (receipt || k.State==CandidateCredentialState.IssuedUnconfirmed))
        {
            var old = await DevelopmentIssuer.IssueAsync(new(k.IssuanceOperationId,k.DeviceId,k.Id,k.SpkiBase64,k.RootCertificatePem,k.NotBeforeUtc,k.NotAfterUtc),default);
            if(k.State==CandidateCredentialState.IssuedUnconfirmed) { k.CertificatePem=old.CertificatePem; k.CertificateSha256=DeviceCertificateProfile.Validate(new(k.IssuanceOperationId,k.DeviceId,k.Id,k.SpkiBase64,k.RootCertificatePem,k.NotBeforeUtc,k.NotAfterUtc),old.CertificatePem!,now); k.IssuedAtUtc=now.AddMinutes(-1); }
        }
        await Db(async db => { db.DeviceEnrollments.Add(e); if(k is not null)db.DeviceCredentials.Add(k);
            db.EnrollmentChallenges.Add(new(){Id=Guid.NewGuid(),EnrollmentId=e.Id,Purpose=ChallengePurpose.Enrollment,CreatedAtUtc=now,
                ExpiresAtUtc=now.AddMinutes(2),NonceHash=DeviceEnrollmentProof.Hash("historical-nonce"),PayloadHash=DeviceEnrollmentProof.Hash("historical-payload")});
            var store=new JaxPower.Ipqc.Api.Services.ProvisioningStore(db,Clock);
            store.Audit(null,d.Id,"EnrollmentInvited",TestAccounts.AdminId,"admin",null,
                new(d.DisplayName,d.Revision,LineId:e.LineId,EnrollmentId:e.Id,EnrollmentState:e.State.ToString(),CredentialId:k?.Id));
            await db.SaveChangesAsync(); return 0; });
        return new(e.Id,d.Id,k?.Id,k?.IssuanceOperationId);
    }
    public static string Csr(ECDsa key, Guid device) => Convert.ToBase64String(new CertificateRequest($"CN=IPQC-{device:D}",key,HashAlgorithmName.SHA256).CreateSigningRequest());
    public static EnrollmentProofInput Sign(EnrollmentChallengeOutput challenge,ECDsa key,DSASignatureFormat format=DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
    { var input=challenge.ProtectedHeader+"."+challenge.Payload;return new(challenge.ChallengeId,input+"."+DeviceEnrollmentProof.Encode(key.SignData(Encoding.ASCII.GetBytes(input),HashAlgorithmName.SHA256,format))); }
    public async Task<EnrollmentOutput> Get(Guid device)
    { var page=await App.Admin.GetFromJsonAsync<ProvisioningPage<EnrollmentOutput>>($"/api/admin/devices/{device}/enrollments");return page!.Items.Single(); }
    public async Task<bool> Process(CancellationToken ct=default)
    {
        using var scope=App.Factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        Issuer.OutsideTransaction=()=>Assert.Null(db.Database.CurrentTransaction);
        try{return await scope.ServiceProvider.GetRequiredService<JaxPower.Ipqc.Api.Services.DeviceCredentialIssuance>().ProcessNextAsync(ct);}
        finally{Issuer.OutsideTransaction=null;}
    }
    public Task<T> Db<T>(Func<IpqcDbContext,Task<T>> read)=>App.Db(read);
    public async Task<DeviceCredential> Candidate()=>await Db(db=>db.DeviceCredentials.AsNoTracking().SingleAsync());
    public async Task<string> State()=>await Db(async db=>JsonSerializer.Serialize(new object[]{
        await db.DeviceEnrollments.AsNoTracking().OrderBy(x=>x.Id).ToListAsync(),await db.EnrollmentChallenges.AsNoTracking().OrderBy(x=>x.Id).ToListAsync(),
        await db.DeviceCredentials.AsNoTracking().OrderBy(x=>x.Id).ToListAsync(),await db.RegisteredDevices.AsNoTracking().OrderBy(x=>x.Id).ToListAsync(),
        await db.ProvisioningAuditEvents.AsNoTracking().OrderBy(x=>x.Id).ToListAsync()}));
    public async ValueTask DisposeAsync(){Bootstrap.Dispose();await App.DisposeAsync();DevelopmentIssuer.Dispose();Directory.Delete(DirectoryPath,true);}
}