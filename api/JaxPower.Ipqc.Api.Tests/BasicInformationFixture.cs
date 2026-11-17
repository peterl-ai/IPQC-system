using System.Net.Http.Json;
using System.Text.Json;
using JaxPower.Ipqc.Api.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

internal static class BasicInformationFixture
{
    public const string Previous = "20261116145540_R3205ConfirmedPhotoPolicies";
    public const string Current = "20261116221536_R3205SharedTaskBasicInformationDraft";
    public static string WriteUrl(DraftSetup s) => s.Root + "/basic-information-batches";
    public static string ReadUrl(DraftSetup s) => s.Root + "/shared-draft/basic-information";
    public static string ReceiptUrl(DraftSetup s, Guid id) => s.Root + $"/basic-information-operations/{id}/receipt";
    public static BasicInformationBatchInput Input(DraftSetup s, long version = 0, BasicInformationField field = BasicInformationField.Customer,
        string? value = "Synthetic customer", BasicInformationReceipt? previous = null, WorkContextIssued? source = null) {
        var c = source ?? s.Source;
        return new(Guid.NewGuid(), 1, s.F.Line1, 2, PersonalSessionFixture.Environment, version,
            new(Guid.NewGuid(), 1, previous is null ? "CaptureBasicInformationField" : "CorrectBasicInformationField", field,
                c.Context.Id, c.Context.UserId, version, version + 1, value,
                previous is null ? null : new(previous.OperationId, previous.ContentHash)), new(c.Context.Id, c.ProofSecret));
    }
    public static async Task<BasicInformationBatchResult> Accept(DraftSetup s, BasicInformationBatchInput input, AuthOutput? actor = null) {
        var response = await s.Client(actor).PostAsJsonAsync(WriteUrl(s), input);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var r = (await response.Content.ReadFromJsonAsync<BasicInformationBatchResult>())!;
        Assert.False(r.CanExecute); Assert.False(r.CanSubmit); Assert.False(r.AcceptsFormalInspection); return r;
    }
    public static async Task<BasicInformationOutput> Read(DraftSetup s, string query = "") {
        var response = await s.Client().GetAsync(ReadUrl(s) + query); Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString()); var r = (await response.Content.ReadFromJsonAsync<BasicInformationOutput>())!;
        Assert.False(r.CanExecute); Assert.False(r.CanSubmit); Assert.False(r.AcceptsFormalInspection); Assert.Equal("OptionalHeaderFactsOnly", r.Completeness);
        Assert.All(r.Fields, x => { Assert.False(x.IsRequired); Assert.False(x.BlocksSubmit); }); return r;
    }
    public static async Task<string> Facts(PersonalSessionFixture f) => await InspectionIdentityMigrationTests.OldFacts(f, true, true, true, true, true) +
        await f.Read(async db => JsonSerializer.Serialize(new {
            Registry = await db.AcceptedOperationIdentities.OrderBy(x => x.TaskId).ThenBy(x => x.OperationId).ToListAsync(),
            Unable = await db.UnableToMeasureOperations.OrderBy(x => x.OperationId).ToListAsync(), UnableState = await db.UnableToMeasureProjections.OrderBy(x => x.InspectionAttemptId).ToListAsync(),
            Bindings = await db.SampleEvidenceOperations.OrderBy(x => x.OperationId).ToListAsync(), BindingState = await db.SampleEvidenceProjections.OrderBy(x => x.BindingId).ToListAsync(),
            Policies = await db.PhotoPolicyDefinitions.OrderBy(x => x.Id).ToListAsync(), Confirmations = await db.PhotoPolicyConfirmations.OrderBy(x => x.Id).ToListAsync(),
            PolicyState = await db.PhotoPolicyStates.OrderBy(x => x.StandardItemId).ToListAsync(), Frozen = await db.FrozenTaskPhotoPolicies.OrderBy(x => x.TaskItemId).ToListAsync(),
            Headers = await db.TaskHeaderOperations.OrderBy(x => x.TaskId).ThenBy(x => x.OperationId).ToListAsync(),
            HeaderState = await db.TaskHeaderProjections.OrderBy(x => x.TaskId).ThenBy(x => x.Field).ToListAsync()
        }));
    public static async Task Counts(DraftSetup s, long version, int operations, int fields) {
        Assert.Equal(version, await DraftEvidenceAssessmentTests.Version(s)); Assert.Equal(operations, await s.F.Read(db => db.TaskHeaderOperations.CountAsync()));
        Assert.Equal(fields, await s.F.Read(db => db.TaskHeaderProjections.CountAsync()));
        Assert.Equal(operations, await s.F.Read(db => db.AcceptedBatchReceipts.CountAsync())); Assert.Equal(operations, await s.F.Read(db => db.SharedAcceptanceAudits.CountAsync()));
        Assert.Equal(0, await s.F.Read(db => db.PatrolTaskSubmissions.CountAsync())); Assert.Equal(0, await s.F.Read(db => db.PatrolTaskReviews.CountAsync()));
        Assert.Equal(0, await s.F.Read(db => db.SharedSamples.CountAsync())); Assert.Equal(0, await s.F.Read(db => db.PatrolTaskItemSamples.CountAsync()));
    }
}
