using JasperFx.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Wolverine;
using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Durability.DeadLetterManagement;
using Wolverine.Postgresql;
using WolverineDemo.Domain;
using WolverineDemo.Infrastructure;
using WolverineDemo.Sagas;
using WolverineDemo.Steps;

var builder = Host.CreateApplicationBuilder(args);


var connectionString = builder.Configuration.GetConnectionString("WolverineDemo")
    ?? "Host=localhost;Port=5432;Database=wolverine_demo;Username=postgres;Password=postgres";

await EnsureDatabaseExistsAsync(connectionString);
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Services.AddSingleton<DocumentRepository>();
builder.Services.AddSingleton<IDocumentTextExtractor, FakeDocumentTextExtractor>();
builder.Services.AddSingleton<IEmbeddingClient, FakeEmbeddingClient>();
builder.Services.AddSingleton<IEmbeddingCacheStore, InMemoryEmbeddingCacheStore>();
builder.Services.AddSingleton<IProgressPublisher, ConsoleProgressPublisher>();

// Demo console owns failure messaging; drop WolverineRuntime Error stacks
// (e.g. event 108 "moved to the error queue").
builder.Logging.AddFilter((provider, category, level) =>
    !(category == "Wolverine.Runtime.WolverineRuntime" && level >= LogLevel.Error));

builder.UseWolverine(opts =>
{
    // Postgres under the hood: durable inbox, outbox, and saga storage, one
    // connection string, no separate broker needed for this scale.
    opts.PersistMessagesWithPostgresql(connectionString);

    // Registering gotcha #1: discovery is opt-in, not opt-out. Disabled here to
    // match the production pattern (where MediatR handlers share the assembly
    // and would otherwise be mistaken for Wolverine handlers) -- not strictly
    // required in this standalone demo, but kept so the real code matches the slide.
    opts.Discovery.DisableConventionalDiscovery();
    opts.Discovery.IncludeType<DocumentIngestionAttempt>();
    opts.Discovery.IncludeType(typeof(ExtractDocumentTextHandler));
    opts.Discovery.IncludeType(typeof(GenerateEmbeddingsBatchHandler));
});

var app = builder.Build();
await app.StartAsync();

await RunMenuAsync(app.Services);

await app.StopAsync();
return;

static async Task EnsureDatabaseExistsAsync(string connectionString)
{
    var csBuilder = new NpgsqlConnectionStringBuilder(connectionString);
    var targetDb = csBuilder.Database;
    csBuilder.Database = "postgres";

    try
    {
        await using var conn = new NpgsqlConnection(csBuilder.ConnectionString);
        await conn.OpenAsync();

        await using var check = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", conn);
        check.Parameters.AddWithValue("name", targetDb!);
        var exists = await check.ExecuteScalarAsync() is not null;

        if (!exists)
        {
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{targetDb}\"", conn);
            await create.ExecuteNonQueryAsync();
            Console.WriteLine($"Created database '{targetDb}'.");
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Could not reach/prepare Postgres at startup: {ex.Message}");
        Console.WriteLine("Fix the connection string in appsettings.json (or user secrets) and re-run.");
        Console.ResetColor();
        Environment.Exit(1);
    }
}

static async Task RunMenuAsync(IServiceProvider services)
{
    var bus = services.GetRequiredService<IMessageBus>();
    var store = services.GetRequiredService<IMessageStore>();

    while (true)
    {
        Console.WriteLine();
        Console.WriteLine("1) Start ingestion (none)");
        Console.WriteLine("2) Start ingestion (flaky)");
        Console.WriteLine("3) Start ingestion (dead)");
        Console.WriteLine("4) Start ingestion (stall -- triggers the ingestion timeout after ~50s)");
        Console.WriteLine("5) View dead letters");
        Console.WriteLine("6) Replay dead letters");
        Console.WriteLine("7) Exit");
        Console.Write("> ");

        switch (Console.ReadLine())
        {
            case "1": await StartIngestionAsync(bus, "none"); break;
            case "2": await StartIngestionAsync(bus, "flaky"); break;
            case "3": await StartIngestionAsync(bus, "dead"); break;
            case "4": await StartIngestionAsync(bus, "stall"); break;
            case "5": await ViewDeadLettersAsync(store); break;
            case "6": await ReplayDeadLettersAsync(store); break;
            case "7": return;
            default: Console.WriteLine("Unrecognized option."); break;
        }
    }
}

static async Task StartIngestionAsync(IMessageBus bus, string failMode)
{
    var id = Guid.NewGuid();
    Console.WriteLine($"Starting ingestion {id} (failMode={failMode})");
    await bus.PublishAsync(new StartDocumentIngestion(id, failMode));
}

static async Task ViewDeadLettersAsync(IMessageStore store)
{
    var result = await store.DeadLetters.QueryAsync(
        new DeadLetterEnvelopeQuery(TimeRange.AllTime()), CancellationToken.None);

    if (result.Envelopes.Count == 0)
    {
        Console.WriteLine("No dead letters.");
        return;
    }

    foreach (var dl in result.Envelopes)
        Console.WriteLine($"  {dl.Id} {dl.MessageType} {dl.ExceptionType}: {dl.ExceptionMessage}");
}

static async Task ReplayDeadLettersAsync(IMessageStore store)
{
    var replayed = await store.DeadLetters.ReplayMatchingAsync(
        new DeadLetterEnvelopeQuery(TimeRange.AllTime()), CancellationToken.None);

    Console.WriteLine($"Replayed {replayed.Affected} message(s).");
}
