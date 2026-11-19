using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using static JaxPower.Ipqc.Api.Tests.OrdinaryNgDetailsFixture;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class OrdinaryNgDetailsConcurrencyTests
{
    [Theory]
    [InlineData("details", true)]
    [InlineData("sample", true)]
    [InlineData("sample", false)]
    [InlineData("removed", true)]
    [InlineData("removed", false)]
    [InlineData("pic", true)]
    [InlineData("pic", false)]
    [InlineData("header", true)]
    [InlineData("header", false)]
    public Task Admitted_writers_share_one_Task_version(string domain, bool detailsFirst) => Cas(false, domain, detailsFirst);

    internal static async Task Cas(bool postgres, string domain, bool detailsFirst)
    {
        using var race = new AdmittedCasRace(["details", "other"]);
        await using var p = await PhotoFixture.Create(postgres, configure: race.Configure);
        await AdmittedCasTestCase.Diagnose(p, race, async () =>
        {
            var f = p.F;
            var fixture = await NgPicSelectionFixture.Setup(f);
            var s = fixture.S;
            var secondScope = s with { Source = fixture.SourceB };
            race.AuthorityByDomain = new Dictionary<string, (Guid, Guid)> {
                ["details"] = (s.A.User.Id, s.A.PersonalSessionId!.Value),
                ["other"] = (s.B.User.Id, s.B.PersonalSessionId!.Value)
            };
            race.Secrets = [s.A.AccessToken, s.B.AccessToken, s.A.RefreshToken!, s.B.RefreshToken!,
                s.Source.ProofSecret, secondScope.Source.ProofSecret, TestAccounts.Password];

            var details = Input(s);
            var sample = secondScope.Batch(secondScope.Capture(new("NG", null, null), version: 1), 1);
            var removal = ItemRemovalTests.Input(secondScope, 1);
            var pic = NgPicSelectionFixture.Input(fixture, source: fixture.SourceB);
            var header = BasicInformationFixture.Input(secondScope, 1);
            var otherDetails = Input(secondScope, reason: "Other accepted NG details");
            (string Url, object Body, Guid Batch, Guid Operation) other = domain switch
            {
                "sample" => (s.Root + "/operation-batches", sample, sample.BatchId, sample.Operations[0].OperationId),
                "removed" => (ItemRemovalTests.Url(s), removal, removal.BatchId, removal.Operation.OperationId),
                "pic" => (NgPicSelectionFixture.Url(s), pic, pic.BatchId, pic.Operation.OperationId),
                "header" => (BasicInformationFixture.WriteUrl(s), header, header.BatchId, header.Operation.OperationId),
                _ => (WriteUrl(s), otherDetails, otherDetails.BatchId, otherDetails.Operation.OperationId)
            };
            const string update = "UPDATE \"SharedDrafts\"";
            var holder = 0;
            var taskLockWaiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Fault.ObserveAsync = (command, _) =>
            {
                if (command.Connection is NpgsqlConnection pg && command.Transaction is not null)
                {
                    if (command.CommandText.Contains(update, StringComparison.Ordinal))
                        Interlocked.CompareExchange(ref holder, pg.ProcessID, 0);
                    if (holder != 0 && holder != pg.ProcessID &&
                        command.CommandText.Contains("FROM \"PatrolTasks\"", StringComparison.Ordinal) &&
                        command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
                        taskLockWaiter.TrySetResult(pg.ProcessID);
                }
                return Task.CompletedTask;
            };
            f.Fault.Arm(update);
            var winner = detailsFirst ? "details" : "other";
            var loser = detailsFirst ? "other" : "details";
            using var client = s.Client();
            using var author = s.Client(s.A);
            var primary = race.Send(author, "details", WriteUrl(s), details);
            var secondary = race.Send(client, "other", other.Url, other.Body);
            try
            {
                await race.AllAdmitted.WaitAsync(TimeSpan.FromSeconds(15));
                race.Require(race.Transactions == 0, "Both contenders must pass real admission before a business transaction starts");
                race.Release(winner);
                await f.Fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                race.Release(loser);
                if (postgres)
                {
                    var pid = await taskLockWaiter.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    var path = await PostgresBlocking.Wait(f.Connection, holder, pid);
                    race.Note("PostgresBlockingPath", new { Holder = holder, Waiter = pid, Path = path }, loser);
                }
                else await race.WriterStarted(loser).WaitAsync(TimeSpan.FromSeconds(10));
                race.Require(!(detailsFirst ? secondary.IsCompleted : primary.IsCompleted), "The competing request must wait for the actual reserved writer");
            }
            finally
            {
                race.ReleaseAll();
                f.Fault.Release.TrySetResult();
                try { await Task.WhenAll(primary, secondary); }
                finally { f.Fault.ObserveAsync = null; }
            }
            using var accepted = detailsFirst ? await primary : await secondary;
            using var rejected = detailsFirst ? await secondary : await primary;
            race.Require(accepted.StatusCode == HttpStatusCode.OK && rejected.StatusCode == HttpStatusCode.Conflict, "Exactly one contender must accept");
            Assert.Contains("DraftVersionConflict", await rejected.Content.ReadAsStringAsync());
            race.Require(race.Transactions == 2 && race.CommitCount(winner) == 1 && race.CommitCount(loser) == 0 &&
                race.CasCount(winner) == 1 && race.CasRows(winner) == 1, "Only the winner advances and commits the real CAS");
            var batch = detailsFirst ? details.BatchId : other.Batch;
            var operation = detailsFirst ? details.Operation.OperationId : other.Operation;
            var losingBatch = detailsFirst ? other.Batch : details.BatchId;
            var losingOperation = detailsFirst ? other.Operation : details.Operation.OperationId;
            Assert.Equal(2, await DraftEvidenceAssessmentTests.Version(s));
            Assert.Equal(2, await f.Read(db => db.SharedAcceptanceAudits.CountAsync()));
            Assert.Equal(2, await f.Read(db => db.AcceptedOperationIdentities.CountAsync()));
            Assert.Equal(2, await f.Read(db => db.AcceptedBatchReceipts.CountAsync()));
            Assert.False(await f.Read(db => db.AcceptedBatchReceipts.AnyAsync(x => x.BatchId == losingBatch)));
            Assert.False(await f.Read(db => db.AcceptedOperationIdentities.AnyAsync(x => x.OperationId == losingOperation)));
            Assert.True(await f.Read(db => db.AcceptedOperationIdentities.AnyAsync(x => x.OperationId == operation)));
            Assert.Equal(detailsFirst || domain == "details" ? 1 : 0, await f.Read(db => db.OrdinaryNgDetailsOperations.CountAsync()));
            if (detailsFirst || domain == "details")
            {
                var receipt = (await accepted.Content.ReadFromJsonAsync<OrdinaryNgDetailsBatchResult>())!.Operation;
                Assert.Equal(Json(receipt), await client.GetStringAsync(ReceiptUrl(s, operation)));
            }
            Assert.Equal(2, (await client.GetFromJsonAsync<DraftBatchReceipt>(CommonUrl(s, batch)))!.DraftVersion);
        });
    }
}
