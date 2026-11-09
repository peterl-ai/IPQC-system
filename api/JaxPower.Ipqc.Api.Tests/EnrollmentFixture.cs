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
    public int Issues { get; private set; }
    public string? Failure { get; private set; }
    public Func<Task>? BeforeIssue { get; set; }
    public Func<DeviceIssuanceRequest, DeviceIssuanceResult, DeviceIssuanceResult>? Transform { get; set; }
    public Action? OutsideTransaction { get; set; }
    public DeviceIssuerProfile Profile => inner.Profile;
    public Task<DeviceIssuanceResult> RecoverAsync(Guid operation, CancellationToken ct)
    { OutsideTransaction?.Invoke(); return Unknown ? Task.FromResult(new DeviceIssuanceResult(IssuerOutcome.Unknown)) : inner.RecoverAsync(operation, ct); }
    public async Task<DeviceIssuanceResult> IssueAsync(DeviceIssuanceRequest request, CancellationToken ct)
    {
        OutsideTransaction?.Invoke(); if (BeforeIssue is not null) await BeforeIssue();
        DeviceIssuanceResult result;
        try { result = await inner.IssueAsync(request, ct); Issues++; }
        catch(Exception ex) { Failure = ex.ToString(); throw; }
        if (LoseResponseOnce) { LoseResponseOnce = false; throw new IOException("Injected lost signer response."); }
        return Transform?.Invoke(request, result) ?? result;
    }
    public Task RevokeAsync(Guid operation, CancellationToken ct) { OutsideTransaction?.Invoke(); return inner.RevokeAsync(operation, ct); }
}
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
        var options = new DeviceEnrollmentOptions { BootstrapEnabled = true, EnvironmentId = "isolated-ipqc-test",
            DevelopmentIssuerEnabled = true, RootPfxPath = Path.Combine(folder, "root.pfx"), RootPassword = password,
            ReceiptDirectory = Path.Combine(folder, "public-receipts"), WorkerEnabled = false };
        using (var rootKey = RSA.Create(2048))
        {
            var rootRequest = new CertificateRequest("CN=IPQC Synthetic Test CA", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            using var root = rootRequest.CreateSelfSigned(clock.GetUtcNow().AddMinutes(-5), clock.GetUtcNow().AddYears(1));
            await File.WriteAllBytesAsync(options.RootPfxPath, root.Export(X509ContentType.Pfx, password));
        }
        var development = new DevelopmentDeviceCertificateIssuer(options, new EnrollmentTestHost()); var issuer = new ControlledIssuer(development);
        var app = await ProvisioningFixture.CreateAsync(failure, services =>
        {
            services.RemoveAll<DeviceEnrollmentOptions>(); services.AddSingleton(options);
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
            services.RemoveAll<IDeviceCertificateIssuer>(); services.AddSingleton<IDeviceCertificateIssuer>(issuer);
        });
        return new() { App = app, Bootstrap = app.Factory.CreateClient(), DirectoryPath = folder, Options = options,
            DevelopmentIssuer = development, Issuer = issuer, Clock = clock };
    }
    public async Task<DeviceOutput> BoundDevice(string code = "A")
    {
        var line = await App.LineAsync(code); var d = await App.DeviceAsync("Synthetic PAD");
        return await App.DeviceAction(d, "bind", new DeviceBindingInput(line.Id, d.Revision));
    }
    public async Task<EnrollmentInvitation> Invite(DeviceOutput d)
    { var r = await App.Admin.PostAsJsonAsync($"/api/admin/devices/{d.Id}/enrollments", new EnrollmentCreateInput(d.Revision)); r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<EnrollmentInvitation>())!; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ECDsa, Dictionary<Guid,string>> Csrs = new();
    public static string Csr(ECDsa key, Guid device)
    {
        var cache = Csrs.GetOrCreateValue(key);
        lock(cache) { if (!cache.TryGetValue(device, out var csr)) cache[device] = csr = Convert.ToBase64String(new CertificateRequest($"CN=IPQC-{device:D}", key, HashAlgorithmName.SHA256).CreateSigningRequest()); return csr; }
    }
    public async Task<EnrollmentChallengeOutput> Challenge(EnrollmentInvitation invite, ECDsa key)
    { var r = await Bootstrap.PostAsJsonAsync($"/bootstrap/v1/enrollments/{invite.Enrollment.Id}/challenge", new EnrollmentChallengeInput(invite.InvitationSecret, Csr(key, invite.Enrollment.DeviceId))); r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<EnrollmentChallengeOutput>())!; }
    public static EnrollmentProofInput Sign(EnrollmentChallengeOutput challenge, ECDsa key, DSASignatureFormat format = DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
    {
        var input = challenge.ProtectedHeader + "." + challenge.Payload;
        return new(challenge.ChallengeId, input + "." + DeviceEnrollmentProof.Encode(key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, format)));
    }
    public async Task<EnrollmentOutput> Prove(EnrollmentInvitation invite, ECDsa key)
    { var challenge = await Challenge(invite, key); var r = await Bootstrap.PostAsJsonAsync($"/bootstrap/v1/enrollments/{invite.Enrollment.Id}/proof", Sign(challenge, key)); r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<EnrollmentOutput>())!; }
    public EnrollmentApproveInput Approval(DeviceOutput d, EnrollmentOutput e, Guid? operation = null) => new(e.Revision, d.Revision, e.LineId, e.SourceBindingVersion, e.CsrSha256!, e.SpkiSha256!, operation ?? Guid.NewGuid());
    public async Task<EnrollmentOutput> Approve(DeviceOutput d, EnrollmentOutput e)
    { var r = await App.Admin.PostAsJsonAsync($"/api/admin/enrollments/{e.Id}/approve", Approval(d, e)); r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<EnrollmentOutput>())!; }
    public async Task<EnrollmentOutput> Get(Guid device)
    { var page = await App.Admin.GetFromJsonAsync<ProvisioningPage<EnrollmentOutput>>($"/api/admin/devices/{device}/enrollments"); return page!.Items.Single(); }
    public async Task<bool> Process()
    {
        using var scope = App.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        Issuer.OutsideTransaction = () => Assert.Null(db.Database.CurrentTransaction);
        try { return await scope.ServiceProvider.GetRequiredService<DeviceCredentialIssuance>().ProcessNextAsync(default); }
        finally { Issuer.OutsideTransaction = null; }
    }
    public async Task<T> Db<T>(Func<IpqcDbContext, Task<T>> read)
    { using var scope = App.Factory.Services.CreateScope(); return await read(scope.ServiceProvider.GetRequiredService<IpqcDbContext>()); }
    public async Task<string> State() => await Db(async db => JsonSerializer.Serialize(new object[] {
        await db.DeviceEnrollments.AsNoTracking().OrderBy(x=>x.Id).ToListAsync(), await db.EnrollmentChallenges.AsNoTracking().OrderBy(x=>x.Id).ToListAsync(),
        await db.DeviceCredentials.AsNoTracking().OrderBy(x=>x.Id).ToListAsync(), await db.RegisteredDevices.AsNoTracking().OrderBy(x=>x.Id).ToListAsync(),
        await db.ProvisioningAuditEvents.AsNoTracking().OrderBy(x=>x.Id).ToListAsync() }));
    public async ValueTask DisposeAsync()
    { Bootstrap.Dispose(); await App.DisposeAsync(); DevelopmentIssuer.Dispose(); Directory.Delete(DirectoryPath, true); }
}
