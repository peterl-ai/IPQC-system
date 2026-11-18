using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

internal static class AdmittedCasTestCase
{
    internal static async Task Run(bool postgres, string first, bool delayedAdmission = false)
    {
        using var race = new AdmittedCasRace { DelayBindingAdmission = delayedAdmission };
        await using var p = await PhotoFixture.Create(postgres, configure: race.Configure);
        await Diagnose(p, race, () => Exercise(p, race, first, delayedAdmission));
    }
    internal static async Task Diagnose(PhotoFixture p, AdmittedCasRace race, Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) {
            race.Failure("TestFailure", error);
            try { race.Note("FailureState", JsonNode.Parse(await State(p.F))); }
            catch (Exception stateError) { race.Failure("FailureStateUnavailable", stateError); }
            throw new Xunit.Sdk.XunitException("CAS/admission test failed; provider=" + (p.F.PostgresRoot is not null ? "PostgreSQL" : "SQLite") + "\n" + race.Report());
        }
    }
    private static async Task Exercise(PhotoFixture p, AdmittedCasRace race, string first, bool delayedAdmission)
    {
        var postgres = p.F.PostgresRoot is not null;
        var u = await SampleEvidenceTests.Setup(p); var s = u.S; var f = p.F;
        race.Actor = s.B.User.Id; race.Family = s.B.PersonalSessionId!.Value;
        race.Secrets = [s.A.AccessToken, s.B.AccessToken, s.A.RefreshToken!, s.B.RefreshToken!, s.Source.ProofSecret, TestAccounts.Password];
        var binding = SampleEvidenceTests.Input(u); var sample = s.Batch(s.Capture(version: 2), 2);
        var removed = ItemRemovalTests.Input(s, 2); var photo = DraftAttachmentTests.Input(s, u.Photo, 2, previous: u.Reference, kind: "DetachAttachment");
        var unable = UnableToMeasureTests.Input(new(s, u.Photo, u.Reference), 2);
        var inputs = new Dictionary<string, (string Url, object Body, Guid Batch, Guid Operation)> {
            ["binding"] = (SampleEvidenceTests.Url(s), binding, binding.BatchId, binding.Operation.OperationId),
            ["sample"] = (s.Root + "/operation-batches", sample, sample.BatchId, sample.Operations[0].OperationId),
            ["removed"] = (ItemRemovalTests.Url(s), removed, removed.BatchId, removed.Operation.OperationId),
            ["photo"] = (DraftAttachmentTests.Url(s), photo, photo.BatchId, photo.Operations[0].OperationId),
            ["unable"] = (UnableToMeasureTests.Url(s), unable, unable.BatchId, unable.Operation.OperationId)
        };
        var before = await State(f); race.Note("Before", JsonNode.Parse(before));
        var holder = 0;
        f.Fault.ObserveAsync = (command, _) => {
            if (command.Connection is NpgsqlConnection pg && command.CommandText.Contains("UPDATE \"SharedDrafts\"", StringComparison.Ordinal)) Interlocked.CompareExchange(ref holder, pg.ProcessID, 0);
            return Task.CompletedTask;
        };
        f.Fault.Arm("UPDATE \"SharedDrafts\"");
        var pending = new Dictionary<string, Task<HttpResponseMessage>>();
        Task<HttpResponseMessage> Start(string d) { var i = inputs[d]; return race.Send(s.Client(), d, i.Url, i.Body); }
        Dictionary<string, HttpResponseMessage> results;
        try {
            foreach (var d in AdmittedCasRace.Domains) pending[d] = Start(d);
            if (delayedAdmission) {
                try {
                    await race.AdmissionReadHeld.WaitAsync(TimeSpan.FromSeconds(15)); await race.FourAdmitted.WaitAsync(TimeSpan.FromSeconds(15));
                    race.Require(race.AdmittedCount == 4, "Exactly four requests should be admitted while the fifth family read is held");
                    race.Require(race.Transactions == 0 && !f.Fault.Entered.Task.IsCompleted && race.CasAttempts == 0,
                        "A business writer started before every contender passed authentication/current-family admission");
                    race.Require(await DraftEvidenceAssessmentTests.Version(s) == 2, "No version may be claimed before the delayed contender is admitted");
                } finally { race.ReleaseAdmissionRead(); }
            }
            await race.AllAdmitted.WaitAsync(TimeSpan.FromSeconds(15));
            race.Require(race.Transactions == 0 && race.CasAttempts == 0, "All five must be admitted before any business transaction");
            race.Release(first); await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            race.Note("WinningTransactionPaused", new { First = first, Entered = true });
            foreach (var d in AdmittedCasRace.Domains.Where(d => d != first)) race.Release(d);
            // These requests run concurrently while the real winning writer is reserved.
            foreach (var d in AdmittedCasRace.Domains.Where(d => d != first)) {
                if (postgres) {
                    var pid = await race.LockingPid(d).WaitAsync(TimeSpan.FromSeconds(10));
                    var path = await PostgresBlocking.Wait(f.Connection, holder, pid);
                    race.Note("PostgresBlockingPath", new { Holder = holder, Waiter = pid, Path = path }, d);
                } else await race.WriterStarted(d).WaitAsync(TimeSpan.FromSeconds(10));
                race.Require(!pending[d].IsCompleted, "A contender completed before the reserved winning transaction was released: " + d);
            }
        } finally {
            race.ReleaseAll(); f.Fault.Release.TrySetResult();
            // Always drain every request before fixture cleanup, including an assertion failure.
            try { await Task.WhenAll(pending.Values); } finally { race.Note("After", JsonNode.Parse(await State(f))); f.Fault.ObserveAsync = null; }
        }
        results = pending.ToDictionary(x => x.Key, x => x.Value.Result);
        race.Require(race.Transactions == 5, "All five admitted requests must acquire a real business transaction");
        foreach (var (domain, response) in results) {
            race.Require(response.StatusCode == (domain == first ? HttpStatusCode.OK : HttpStatusCode.Conflict), "Wrong response for " + domain);
            race.Require(race.CommitCount(domain) == (domain == first ? 1 : 0), "Only the winner may commit: " + domain);
            if (domain == first) { race.Require(race.CasCount(domain) == 1 && race.CasRows(domain) == 1, "Winner must claim exactly one version with real CAS SQL"); continue; }
            using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var expected = (first, domain) switch {
                ("removed", "sample" or "unable") => "DraftItemRemoved",
                ("removed" or "unable", "binding") => "BindingItemNotApplicable",
                ("photo", "binding") => "BindingPhotoDetached",
                ("photo", "unable") => "UnableEvidenceDetached",
                _ => "DraftVersionConflict"
            };
            race.Require(error.RootElement.GetProperty("code").GetString() == expected, "Wrong business rejection for " + domain + "; expected " + expected);
            race.Require(expected == "DraftVersionConflict" ? race.CasCount(domain) == 1 && race.CasRows(domain) == 0 : race.CasCount(domain) == 0 && race.CasRows(domain) is null,
                "Distinguish real zero-row CAS from a valid earlier business rejection: " + domain);
        }
        var winner = inputs[first];
        race.Require(await DraftEvidenceAssessmentTests.Version(s) == 3, "Exactly one version must be claimed");
        race.Require(await f.Read(db => db.AcceptedBatchReceipts.CountAsync()) == 3 && await f.Read(db => db.SharedAcceptanceAudits.CountAsync()) == 3 && await f.Read(db => db.AcceptedOperationIdentities.CountAsync()) == 3, "Receipt/audit/identity must each increase by exactly one");
        race.Require(await f.Read(db => db.AcceptedBatchReceipts.AnyAsync(x => x.BatchId == winner.Batch && x.DraftVersion == 3)) &&
            await f.Read(db => db.SharedAcceptanceAudits.AnyAsync(x => x.BatchId == winner.Batch && x.BeforeVersion == 2 && x.AfterVersion == 3 && x.NewOperationCount == 1)) &&
            await f.Read(db => db.AcceptedOperationIdentities.AnyAsync(x => x.OperationId == winner.Operation)), "Only the intended winner may own the new durable facts");
        race.Require(await f.Read(db => db.ContributionOperations.CountAsync()) == (first == "sample" ? 2 : 1) &&
            await f.Read(db => db.DraftAttachmentOperations.CountAsync()) == (first == "photo" ? 2 : 1) &&
            await f.Read(db => db.ItemRemovalOperations.CountAsync()) == (first == "removed" ? 1 : 0) &&
            await f.Read(db => db.UnableToMeasureOperations.CountAsync()) == (first == "unable" ? 1 : 0) &&
            await f.Read(db => db.SampleEvidenceOperations.CountAsync()) == (first == "binding" ? 1 : 0), "No losing domain may append an operation");
        using var original = JsonDocument.Parse(await results[first].Content.ReadAsStringAsync());
        var batchReceipt = await s.Client().GetStringAsync(s.Root + $"/operation-batches/{winner.Batch}/receipt");
        race.Require(JsonNode.DeepEquals(JsonNode.Parse(original.RootElement.GetProperty("batchReceipt").GetRawText()), JsonNode.Parse(batchReceipt)), "Recovered common receipt must equal the original");
        var op = original.RootElement.TryGetProperty("operation", out var one) ? one : original.RootElement.GetProperty("operations").EnumerateArray().Single(x => x.GetProperty("operationId").GetGuid() == winner.Operation);
        var receipt = await s.Client().GetStringAsync(s.Root + $"/operations/{winner.Operation}/receipt");
        race.Require(JsonNode.DeepEquals(JsonNode.Parse(op.GetRawText()), JsonNode.Parse(receipt)), "Recovered typed receipt must equal the original");
        var acceptedState = await State(f);
        var replay = await s.Client().PostAsJsonAsync(winner.Url, winner.Body);
        race.Require(replay.StatusCode == HttpStatusCode.OK, "Exact winner replay must recover200");
        using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync()); race.Require(replayJson.RootElement.GetProperty("replayOnly").GetBoolean(), "Known operation must replay, not accept again");
        race.Require(batchReceipt == await s.Client().GetStringAsync(s.Root + $"/operation-batches/{winner.Batch}/receipt") && acceptedState == await State(f), "Replay must not rewrite receipts, identity, audit or domain rows");
    }
    internal static Task<string> State(PersonalSessionFixture f) => f.Read(async db => JsonSerializer.Serialize(new {
        Versions = await db.SharedDrafts.OrderBy(x => x.TaskId).Select(x => new { x.TaskId, x.DraftVersion }).ToListAsync(),
        Receipts = await db.AcceptedBatchReceipts.OrderBy(x => x.DraftVersion).ToListAsync(),
        Audits = await db.SharedAcceptanceAudits.OrderBy(x => x.AfterVersion).ToListAsync(),
        Identities = await db.AcceptedOperationIdentities.OrderBy(x => x.Domain).ThenBy(x => x.OperationId).ToListAsync(),
        Samples = await db.ContributionOperations.OrderBy(x => x.OperationId).ToListAsync(),
        Removed = await db.ItemRemovalOperations.OrderBy(x => x.OperationId).ToListAsync(),
        Photos = await db.DraftAttachmentOperations.OrderBy(x => x.OperationId).ToListAsync(),
        Unable = await db.UnableToMeasureOperations.OrderBy(x => x.OperationId).ToListAsync(),
        Bindings = await db.SampleEvidenceOperations.OrderBy(x => x.OperationId).ToListAsync()
    }));
}
