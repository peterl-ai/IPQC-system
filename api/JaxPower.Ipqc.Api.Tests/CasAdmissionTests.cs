using System.Net;
using System.Net.Http.Json;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class CasAdmissionTests
{
    [Fact] public Task Delayed_current_family_read_cannot_be_overtaken_by_business_writer() => AdmittedCasTestCase.Run(false, "sample", true);

    [Fact] public Task Endpoint_gate_preserves_real_JWT_role_and_family_authority() => Authority(false);
    internal static async Task Authority(bool postgres) {
        // Use a host with the wrapper/interceptors installed; no permission is granted by the wrapper.
        using var race = new AdmittedCasRace { ObserveOnly = true };
        await using var p = await PhotoFixture.Create(postgres, configure: race.Configure);
        await AdmittedCasTestCase.Diagnose(p, race, async () => {
        var u = await SampleEvidenceTests.Setup(p); var s = u.S; var f = p.F;
        race.Actor = s.B.User.Id; race.Family = s.B.PersonalSessionId!.Value; race.Secrets = [s.A.AccessToken, s.B.AccessToken, s.A.RefreshToken!, s.B.RefreshToken!, s.Source.ProofSecret];
        var pqe = await f.Login("pqe");
        var noFamily = f.Token(s.B, cs => cs.Where(c => c.Type != PersonalAccess.FamilyClaim).ToList());
        var input = SampleEvidenceTests.Input(u); var before = await AdmittedCasTestCase.State(f);
        foreach (var (client, status) in new[] { (f.Anonymous, HttpStatusCode.Unauthorized), (s.Client(pqe), HttpStatusCode.Forbidden), (f.Client(noFamily), HttpStatusCode.Unauthorized), (f.Client(s.Source.ProofSecret), HttpStatusCode.Unauthorized) }) {
            var r = await race.Send(client, "binding", SampleEvidenceTests.Url(s), input); race.Require(r.StatusCode == status, "Endpoint wrapper must preserve existing admission rejection");
        }
        (await s.Client().PostAsJsonAsync("/api/auth/logout", new RefreshInput(s.B.RefreshToken))).EnsureSuccessStatusCode();
        race.Require((await race.Send(s.Client(), "binding", SampleEvidenceTests.Url(s), input)).StatusCode == HttpStatusCode.Unauthorized, "Closed family must not reach the gate");
        race.Require(race.AdmittedCount == 0 && race.Transactions == 0 && race.CasAttempts == 0 && before == await AdmittedCasTestCase.State(f), "Rejected callers cannot create business facts or enter the gate");
        });
    }

    [Fact] public async Task Real_pre_admission_SQLite_lock_has_no_acceptance_and_original_ids_recover() {
        using var race = new AdmittedCasRace { ObserveOnly = true };
        await using var p = await PhotoFixture.Create(configure: race.Configure);
        await AdmittedCasTestCase.Diagnose(p, race, async () => {
        var u = await SampleEvidenceTests.Setup(p); var s = u.S; var f = p.F;
        race.Actor = s.B.User.Id; race.Family = s.B.PersonalSessionId!.Value; race.Secrets = [s.A.AccessToken, s.B.AccessToken, s.A.RefreshToken!, s.B.RefreshToken!, s.Source.ProofSecret];
        var input = SampleEvidenceTests.Input(u); var before = await AdmittedCasTestCase.State(f);
        race.Note("Before", System.Text.Json.Nodes.JsonNode.Parse(before));
        // Clear only this disposable database's idle pool before obtaining a real exclusive WAL lock.
        using (var pool = new SqliteConnection(f.Connection)) SqliteConnection.ClearPool(pool);
        var cs = new SqliteConnectionStringBuilder(f.Connection) { Pooling = false };
        await using (var holder = new SqliteConnection(cs.ConnectionString)) {
            await holder.OpenAsync(); await using var command = holder.CreateCommand(); command.CommandText = "PRAGMA locking_mode=EXCLUSIVE";
            Assert.Equal("exclusive", await command.ExecuteScalarAsync()); command.CommandText = "BEGIN EXCLUSIVE"; await command.ExecuteNonQueryAsync();
            try {
                var r = await race.Send(s.Client(), "binding", SampleEvidenceTests.Url(s), input);
                race.Require(r.StatusCode == HttpStatusCode.ServiceUnavailable, "A real pre-admission lock must report temporary unavailability, not a CAS conflict");
                var json = await r.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
                race.Require(json!["code"]!.GetValue<string>() == "PersonalAccessTemporarilyUnavailable", "Unexpected availability classification");
                race.Require(race.AdmittedCount == 0 && race.Transactions == 0 && race.CasAttempts == 0, "The locked request must never reach the admitted/business frontier");
            } finally { command.CommandText = "ROLLBACK"; await command.ExecuteNonQueryAsync(); }
        }
        race.Require(before == await AdmittedCasTestCase.State(f), "Controlled pre-admission failure must append no accepted fact");
        //404 is a point-in-time lookup, not a general guarantee about all unknown-response failures.
        race.Require((await s.Client().GetAsync(s.Root + $"/operations/{input.Operation.OperationId}/receipt")).StatusCode == HttpStatusCode.NotFound, "No receipt should exist for this proven pre-admission failure");
        var accepted = await SampleEvidenceTests.Accept(u, input); race.Require(!accepted.ReplayOnly, "Original unchanged IDs must be accepted once after release");
        var receipt = await s.Client().GetFromJsonAsync<SampleEvidenceReceipt>(s.Root + $"/operations/{input.Operation.OperationId}/receipt"); race.Require(accepted.Operation == receipt, "Recovered receipt must equal the original acceptance");
        var state = await AdmittedCasTestCase.State(f); var replay = await SampleEvidenceTests.Accept(u, input with { SourceProof = null });
        race.Require(replay.ReplayOnly && replay.Operation == accepted.Operation && state == await AdmittedCasTestCase.State(f), "Exact replay must preserve original receipt and all accepted state");
        race.Require(await DraftEvidenceAssessmentTests.Version(s) == 3 && await f.Read(db => db.AcceptedBatchReceipts.CountAsync()) == 3 && await f.Read(db => db.SharedAcceptanceAudits.CountAsync()) == 3 && await f.Read(db => db.AcceptedOperationIdentities.CountAsync()) == 3, "Recovery must not duplicate work");
        race.Note("After", System.Text.Json.Nodes.JsonNode.Parse(await AdmittedCasTestCase.State(f)));
        });
    }

    [Fact] public async Task Observer_captures_real_server_exception_chain_without_changing_500_or_rollback() {
        using var race = new AdmittedCasRace { ObserveOnly = true };
        await using var p = await PhotoFixture.Create(configure: race.Configure);
        await AdmittedCasTestCase.Diagnose(p, race, async () => {
            var u = await SampleEvidenceTests.Setup(p); var s = u.S; var f = p.F;
            race.Actor = s.B.User.Id; race.Family = s.B.PersonalSessionId!.Value;
            race.Secrets = [s.A.AccessToken, s.B.AccessToken, s.A.RefreshToken!, s.B.RefreshToken!, s.Source.ProofSecret, TestAccounts.Password];
            var before = await AdmittedCasTestCase.State(f); var injected = 0;
            f.Fault.ObserveAsync = (command, _) => {
                if (command.CommandText.Contains("INSERT INTO \"SampleEvidenceOperations\"", StringComparison.Ordinal) && Interlocked.Exchange(ref injected, 1) == 0)
                    throw new InvalidOperationException("Synthetic observer control " + s.Source.ProofSecret, new IOException("Synthetic inner control"));
                return Task.CompletedTask;
            };
            HttpResponseMessage response;
            try { response = await race.Send(s.Client(), "binding", SampleEvidenceTests.Url(s), SampleEvidenceTests.Input(u)); }
            finally { f.Fault.ObserveAsync = null; }
            race.Require(response.StatusCode == HttpStatusCode.InternalServerError && injected == 1, "Synthetic unhandled exception must retain real HTTP500 mapping");
            using var report = System.Text.Json.JsonDocument.Parse(race.Report());
            var failure = report.RootElement.GetProperty("Events").EnumerateArray().Single(x => x.GetProperty("Stage").GetString() == "UnhandledException");
            var detail = failure.GetProperty("Detail"); var exception = detail.GetProperty("Exception");
            race.Require(failure.GetProperty("Domain").GetString() == "binding" && !string.IsNullOrEmpty(detail.GetProperty("HttpTrace").GetString()), "Server exception must be correlated to domain and HTTP trace");
            var injectedException = exception.GetProperty("Inner");
            race.Require(exception.GetProperty("Type").GetString() == typeof(DbUpdateException).FullName &&
                injectedException.GetProperty("Stack").GetString()!.Contains(nameof(Observer_captures_real_server_exception_chain_without_changing_500_or_rollback), StringComparison.Ordinal) &&
                injectedException.GetProperty("Inner").GetProperty("Type").GetString() == typeof(IOException).FullName, "Actual EF wrapper, injection stack and inner chain must survive diagnostics");
            race.Require(!race.Report().Contains(s.Source.ProofSecret, StringComparison.Ordinal) && injectedException.GetProperty("Message").GetString()!.Contains("[REDACTED]", StringComparison.Ordinal), "Diagnostics must redact proof secrets");
            race.Require(before == await AdmittedCasTestCase.State(f), "Observer must preserve rollback and append no accepted fact");
        });
    }
}
