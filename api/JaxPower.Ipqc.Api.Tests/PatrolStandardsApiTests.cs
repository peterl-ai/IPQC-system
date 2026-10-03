using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using JaxPower.Ipqc.Api.Contracts;
using JaxPower.Ipqc.Api.Data;
using JaxPower.Ipqc.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace JaxPower.Ipqc.Api.Tests;

public sealed class PatrolStandardsApiTests
{
    private static StandardInput Draft(string name = "Standard A", string line = "Line 1", int items = 1) =>
        new(name, "F1", "Factory", "W1", "L1", line, "M1",
            Enumerable.Range(1, items).Select(i => new ItemInput($"P{i}", "Process", "Category", "Check", "Content",
                "<", "10", ">", "1", "Visual", "Every shift", "2", "Photo", "Major")).ToList());

    [Fact]
    public async Task Create_get_filter_paginate_update_and_delete_persist()
    {
        await using var app = await TestApp.CreateAsync();
        var client = app.Client;
        var createInput = Draft();
        var clientClaimedItemId = Guid.NewGuid();
        createInput.InspectionItems![0] = createInput.InspectionItems[0] with { Id = clientClaimedItemId };
        var created = await Create(client, createInput);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Single(created.InspectionItems);
        Assert.NotEqual(clientClaimedItemId, created.InspectionItems[0].Id);
        Assert.Equal(1, created.InspectionItems[0].SequenceNo);
        Assert.Equal("admin", created.CreatedBy);
        Assert.Equal(DateTimeKind.Utc, created.CreatedAtUtc.Kind);
        var got = await client.GetFromJsonAsync<StandardOutput>($"/api/patrol-standards/{created.Id}");
        Assert.Equal("Standard A", got!.PatrolStandardName);
        Assert.Equal(created.InspectionItems.Select(x => x.Id), got.InspectionItems.Select(x => x.Id));
        Assert.Equal([1], got.InspectionItems.Select(x => x.SequenceNo).ToArray());

        await Create(client, Draft("Standard B", "Line 2", 2));
        var filteredResponse = await client.GetAsync("/api/patrol-standards?standardName=Standard%20A&factoryCode=F1&lineCode=L1");
        var filteredJson = JsonDocument.Parse(await filteredResponse.Content.ReadAsStringAsync());
        Assert.False(filteredJson.RootElement.GetProperty("items")[0].TryGetProperty("inspectionItems", out _));
        var filtered = await filteredResponse.Content.ReadFromJsonAsync<PagedStandards>();
        Assert.Single(filtered!.Items);
        var page = await client.GetFromJsonAsync<PagedStandards>("/api/patrol-standards?page=2&pageSize=1");
        Assert.Equal(2, page!.Total);
        Assert.Single(page.Items);
        Assert.Equal(2, page.Page);

        var updateInput = Draft("Updated", items: 2);
        updateInput.InspectionItems![0] = updateInput.InspectionItems[0] with { Id = created.InspectionItems[0].Id };
        var update = await client.PutAsJsonAsync($"/api/patrol-standards/{created.Id}", updateInput);
        Assert.True(update.IsSuccessStatusCode, await update.Content.ReadAsStringAsync());
        var updated = await update.Content.ReadFromJsonAsync<StandardOutput>();
        Assert.Equal("Updated", updated!.PatrolStandardName);
        Assert.Equal(2, updated.InspectionItems.Count);
        Assert.Equal([1, 2], updated.InspectionItems.Select(x => x.SequenceNo).ToArray());
        Assert.Equal(created.InspectionItems[0].Id, updated.InspectionItems[0].Id);
        Assert.NotEqual(Guid.Empty, updated.InspectionItems[1].Id);
        Assert.Equal(created.CreatedAtUtc, updated.CreatedAtUtc);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/patrol-standards/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/patrol-standards/{created.Id}")).StatusCode);
        using var scope = app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        Assert.Equal(0, await db.PatrolStandardItems.CountAsync(x => x.PatrolStandardId == created.Id));
    }

    [Fact]
    public async Task Update_preserves_edits_reorders_adds_and_removes_item_identity()
    {
        await using var app = await TestApp.CreateAsync();
        var created = await Create(app.Client, Draft(items: 2));
        var firstId = created.InspectionItems[0].Id;
        var secondId = created.InspectionItems[1].Id;

        var unchanged = await Update(app.Client, created.Id, ToInput(created));
        Assert.Equal([firstId, secondId], unchanged.InspectionItems.Select(x => x.Id).ToArray());

        var reorderedInput = ToInput(unchanged);
        reorderedInput.InspectionItems!.Reverse();
        reorderedInput.InspectionItems[0] = reorderedInput.InspectionItems[0] with { ProcessName = "Edited process" };
        reorderedInput.InspectionItems.Add(Draft(items: 1).InspectionItems![0] with { Id = Guid.NewGuid(), ProcessCode = "NEW" });
        var reordered = await Update(app.Client, created.Id, reorderedInput);
        Assert.Equal([secondId, firstId], reordered.InspectionItems.Take(2).Select(x => x.Id).ToArray());
        Assert.Equal([1, 2, 3], reordered.InspectionItems.Select(x => x.SequenceNo).ToArray());
        Assert.Equal("Edited process", reordered.InspectionItems[0].ProcessName);
        Assert.NotEqual(reorderedInput.InspectionItems[2].Id, reordered.InspectionItems[2].Id);
        Assert.DoesNotContain(reordered.InspectionItems[2].Id, new[] { firstId, secondId });

        var removeInput = ToInput(reordered) with { InspectionItems = [ToInput(reordered.InspectionItems[0])] };
        var removed = await Update(app.Client, created.Id, removeInput);
        Assert.Single(removed.InspectionItems);
        Assert.Equal(secondId, removed.InspectionItems[0].Id);
        using var scope = app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IpqcDbContext>();
        Assert.False(await db.PatrolStandardItems.AnyAsync(x => x.Id == firstId));
        Assert.Equal(1, await db.PatrolStandardItems.CountAsync(x => x.PatrolStandardId == created.Id));
    }

    [Fact]
    public async Task Update_cannot_hijack_an_item_from_another_standard()
    {
        await using var app = await TestApp.CreateAsync();
        var target = await Create(app.Client, Draft("Target"));
        var other = await Create(app.Client, Draft("Other"));
        var malicious = ToInput(target);
        malicious.InspectionItems![0] = malicious.InspectionItems[0] with {
            Id = other.InspectionItems[0].Id, ProcessCode = "ATTEMPTED-HIJACK"
        };

        var updated = await Update(app.Client, target.Id, malicious);
        Assert.NotEqual(other.InspectionItems[0].Id, updated.InspectionItems[0].Id);
        Assert.NotEqual(target.InspectionItems[0].Id, updated.InspectionItems[0].Id);
        var untouched = await app.Client.GetFromJsonAsync<StandardOutput>($"/api/patrol-standards/{other.Id}");
        Assert.Equal(other.InspectionItems[0].Id, untouched!.InspectionItems[0].Id);
        Assert.Equal(other.InspectionItems[0].ProcessCode, untouched.InspectionItems[0].ProcessCode);
    }

    [Theory]
    [InlineData("", "Line 1")]
    [InlineData("  ", "Line 1")]
    [InlineData("Standard", "")]
    [InlineData("Standard", "   ")]
    public async Task Required_fields_reject_empty_and_whitespace(string name, string line)
    {
        await using var app = await TestApp.CreateAsync();
        var result = await app.Client.PostAsJsonAsync("/api/patrol-standards", Draft(name, line));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        var list = await app.Client.GetFromJsonAsync<PagedStandards>("/api/patrol-standards");
        Assert.Equal(0, list!.Total);
    }

    [Fact]
    public async Task Optional_fields_and_no_items_are_allowed_but_empty_item_is_not()
    {
        await using var app = await TestApp.CreateAsync();
        var input = new StandardInput("Only required", null, null, null, null, "Line", null, []);
        var created = await Create(app.Client, input);
        Assert.Empty(created.InspectionItems);
        var bad = input with { InspectionItems = [new ItemInput(null, null, null, null, null, null, null, null, null, null, null, null, null, null)] };
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.PostAsJsonAsync("/api/patrol-standards", bad)).StatusCode);
    }

    [Fact]
    public async Task Copy_generates_new_aggregate_and_child_ids_without_mutating_source()
    {
        await using var app = await TestApp.CreateAsync();
        var source = await Create(app.Client, Draft(items: 2));
        var copiedResponse = await app.Client.PostAsJsonAsync($"/api/patrol-standards/{source.Id}/copy", new CopyInput("Independent copy"));
        copiedResponse.EnsureSuccessStatusCode();
        var copied = (await copiedResponse.Content.ReadFromJsonAsync<StandardOutput>())!;
        Assert.NotEqual(source.Id, copied.Id);
        Assert.Equal("Independent copy", copied.PatrolStandardName);
        Assert.Equal(source.LineName, copied.LineName);
        Assert.Equal(source.InspectionItems.Select(x => x.ProcessCode), copied.InspectionItems.Select(x => x.ProcessCode));
        Assert.All(copied.InspectionItems.Zip(source.InspectionItems), pair => Assert.NotEqual(pair.Second.Id, pair.First.Id));
        var unchanged = await app.Client.GetFromJsonAsync<StandardOutput>($"/api/patrol-standards/{source.Id}");
        Assert.Equal(source.InspectionItems.Select(x => x.Id), unchanged!.InspectionItems.Select(x => x.Id));
        Assert.Equal(source.PatrolStandardName, unchanged.PatrolStandardName);
    }

    [Fact]
    public async Task Not_found_and_development_role_are_enforced()
    {
        await using var app = await TestApp.CreateAsync();
        var id = Guid.NewGuid();
        var notFound = await app.Client.GetAsync($"/api/patrol-standards/{id}");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal("application/problem+json", notFound.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.DeleteAsync($"/api/patrol-standards/{id}")).StatusCode);
        var ipqa = app.Factory.CreateClient();
        ipqa.DefaultRequestHeaders.Add("X-Dev-Role", "ipqa");
        var forbidden = await ipqa.GetAsync("/api/patrol-standards");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("application/problem+json", forbidden.Content.Headers.ContentType?.MediaType);
        var pqe = app.Factory.CreateClient();
        pqe.DefaultRequestHeaders.Add("X-Dev-Role", "pqe");
        Assert.Equal(HttpStatusCode.OK, (await pqe.GetAsync("/api/patrol-standards")).StatusCode);
    }

    [Fact]
    public async Task Template_import_export_and_invalid_import_are_transactional()
    {
        await using var app = await TestApp.CreateAsync();
        var template = await app.Client.GetByteArrayAsync("/api/patrol-standards/template");
        using (var workbook = new XLWorkbook(new MemoryStream(template)))
        {
            var sheet = workbook.Worksheet("Patrol Standard");
            Assert.Equal("Line Name", sheet.Cell(5, 1).GetString());
            Assert.Equal("Defect Level", sheet.Cell(9, 14).GetString());
        }
        var invalid = await Import(app.Client, "bad.xls", template);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        invalid = await Import(app.Client, "bad.xlsx", [1, 2, 3]);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var edited = new XLWorkbook(new MemoryStream(template));
        var ws = edited.Worksheet("Patrol Standard");
        ws.Cell(5, 2).Value = "Line 8";
        ws.Cell(6, 2).Value = "Imported";
        ws.Cell(10, 1).Value = "P1";
        ws.Cell(12, 1).Value = "P2";
        using var bytes = new MemoryStream();
        edited.SaveAs(bytes);
        var validBytes = bytes.ToArray();
        var importedResponse = await Import(app.Client, "standard.xlsx", validBytes);
        Assert.Equal(HttpStatusCode.Created, importedResponse.StatusCode);
        var imported = (await importedResponse.Content.ReadFromJsonAsync<StandardOutput>())!;
        Assert.Equal(["P1", "P2"], imported.InspectionItems.Select(x => x.ProcessCode).ToArray());
        Assert.Equal([1, 2], imported.InspectionItems.Select(x => x.SequenceNo).ToArray());

        ws.Cell(12, 1).Value = new string('X', 2001);
        using var lateInvalidBytes = new MemoryStream();
        edited.SaveAs(lateInvalidBytes);
        Assert.Equal(HttpStatusCode.BadRequest, (await Import(app.Client, "late-invalid.xlsx", lateInvalidBytes.ToArray())).StatusCode);
        ws.Cell(12, 1).Value = "P2";

        ws.Cell(5, 2).Value = " ";
        using var badBytes = new MemoryStream();
        edited.SaveAs(badBytes);
        Assert.Equal(HttpStatusCode.BadRequest, (await Import(app.Client, "invalid.xlsx", badBytes.ToArray())).StatusCode);
        ws.Cell(5, 2).Value = "Line 8";
        ws.Cell(6, 2).Value = " ";
        using var missingNameBytes = new MemoryStream();
        edited.SaveAs(missingNameBytes);
        Assert.Equal(HttpStatusCode.BadRequest, (await Import(app.Client, "missing-name.xlsx", missingNameBytes.ToArray())).StatusCode);
        var list = await app.Client.GetFromJsonAsync<PagedStandards>("/api/patrol-standards");
        Assert.Equal(1, list!.Total);

        var export = await app.Client.GetByteArrayAsync($"/api/patrol-standards/export?id={imported.Id}");
        using var exported = new XLWorkbook(new MemoryStream(export));
        Assert.Equal("Imported", exported.Worksheet(1).Cell(6, 2).GetString());
        Assert.Equal("P2", exported.Worksheet(1).Cell(11, 1).GetString());
    }

    private static async Task<StandardOutput> Create(HttpClient client, StandardInput input)
    {
        var result = await client.PostAsJsonAsync("/api/patrol-standards", input);
        result.EnsureSuccessStatusCode();
        return (await result.Content.ReadFromJsonAsync<StandardOutput>())!;
    }

    private static async Task<StandardOutput> Update(HttpClient client, Guid id, StandardInput input)
    {
        var result = await client.PutAsJsonAsync($"/api/patrol-standards/{id}", input);
        Assert.True(result.IsSuccessStatusCode, await result.Content.ReadAsStringAsync());
        return (await result.Content.ReadFromJsonAsync<StandardOutput>())!;
    }

    private static StandardInput ToInput(StandardOutput standard) => new(
        standard.PatrolStandardName, standard.FactoryCode, standard.FactoryName, standard.WorkshopCode,
        standard.LineCode, standard.LineName, standard.MaterialCode,
        standard.InspectionItems.Select(ToInput).ToList());

    private static ItemInput ToInput(ItemOutput item) => new(
        item.ProcessCode, item.ProcessName, item.InspectionItemCategory, item.InspectionItem,
        item.InspectionContent, item.UpperLimitOperator, item.UpperLimitValue, item.LowerLimitOperator,
        item.LowerLimitValue, item.InspectionType, item.SamplingPlan, item.SampleCount,
        item.PhotoRequirement, item.DefectLevel, item.Id);

    private static Task<HttpResponseMessage> Import(HttpClient client, string name, byte[] bytes)
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "file", name);
        return client.PostAsync("/api/patrol-standards/import", content);
    }

    private sealed class TestApp(WebApplicationFactory<Program> factory, HttpClient client, string dbPath) : IAsyncDisposable
    {
        public WebApplicationFactory<Program> Factory { get; } = factory;
        public HttpClient Client { get; } = client;

        public static async Task<TestApp> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"ipqc-test-{Guid.NewGuid():N}.db");
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
            {
                web.UseEnvironment("Development");
                web.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<IpqcDbContext>>();
                    services.AddDbContext<IpqcDbContext>(options => options.UseSqlite($"Data Source={path}"));
                });
            });
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IpqcDbContext>().Database.MigrateAsync();
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Dev-Role", "admin");
            return new TestApp(factory, client, path);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
