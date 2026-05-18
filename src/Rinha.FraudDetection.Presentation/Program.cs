using System.Buffers;
using System.Globalization;
using System.Runtime;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using Rinha.FraudDetection.Application.Interfaces;
using Rinha.FraudDetection.Application.Models;
using Rinha.FraudDetection.Application.UseCases;
using Rinha.FraudDetection.Infrastructure.Resources;
using Rinha.FraudDetection.Infrastructure.Startup;
using Rinha.FraudDetection.Infrastructure.Vectorization;
using Rinha.FraudDetection.Presentation.Contracts;
using Rinha.FraudDetection.Presentation.Fast;
using Rinha.FraudDetection.Infrastructure.Index;

// ── GC ──────────────────────────────────────────────────────────────────────
// SustainedLowLatency por padrão — evita pausas longas do GC sob carga alta.
// Em produção com 1 CPU, pausas do GC são especialmente caras porque bloqueiam
// a única thread disponível.
var gcMode = Environment.GetEnvironmentVariable("GC_LATENCY_MODE");
GCSettings.LatencyMode = gcMode?.ToLowerInvariant() switch
{
    "batch"              => GCLatencyMode.Batch,
    "interactive"        => GCLatencyMode.Interactive,
    "low-latency"        => GCLatencyMode.LowLatency,
    "no-gc-region"       => GCLatencyMode.NoGCRegion,
    _                    => GCLatencyMode.SustainedLowLatency // default seguro
};

// ── ThreadPool ───────────────────────────────────────────────────────────────
// Com 1 CPU e 250 VUs, o gargalo é I/O bound (leitura de body + escrita de
// resposta), não CPU bound. ThreadPool pequeno causa starvation: tasks aguardam
// na fila enquanto outras threads estão bloqueadas esperando I/O.
//
// Regra prática: min = número esperado de requests simultâneos que podem ficar
// na fila aguardando I/O. Com 250 VUs divididos por 2 instâncias = ~125 por
// instância. Valores muito altos desperdiçam memória (cada thread ~1MB de stack).
//
// Se não configurado via env, usa 32 workers (bom equilíbrio para 1 CPU/250 VUs).
var tpMin   = ParseEnvInt("TP_MIN_WORKERS",  32);
var tpMax   = ParseEnvInt("TP_MAX_WORKERS", 128);
var tpMinIo = ParseEnvInt("TP_MIN_IO",       32);
var tpMaxIo = ParseEnvInt("TP_MAX_IO",      128);
ThreadPool.SetMinThreads(tpMin, tpMinIo);
ThreadPool.SetMaxThreads(tpMax, tpMaxIo);

// ── Builder ──────────────────────────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);

// ── Logging — CRÍTICO para performance ───────────────────────────────────────
// O logging verboso que aparece nos seus logs (Diagnostics[1], Routing[0], etc.)
// é do Microsoft.AspNetCore.* e consome CPU significativa sob carga.
// Em produção, filtre tudo acima de Warning.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(opts =>
{
    // Formato simples = menos alocação por log
    opts.FormatterName = "simple";
});
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddFilter("Microsoft",             LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore",  LogLevel.Warning);
builder.Logging.AddFilter("System",                LogLevel.Warning);
// Mantenha seus próprios logs em Information durante testes, Warning em prod
builder.Logging.AddFilter("Rinha",                 LogLevel.Information);

// Ou via appsettings.json / env var ASPNETCORE_LOGGING__LOGLEVEL__DEFAULT=Warning

// ── Kestrel ──────────────────────────────────────────────────────────────────
var udsPath = Environment.GetEnvironmentVariable("UDS_PATH");

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.AllowSynchronousIO = false;

    // Aumentar o tamanho do buffer de resposta reduz flushes parciais
    // Padrão Kestrel = 4096 bytes. Para respostas JSON pequenas está OK,
    // mas para pipelining ajuda ter um pouco mais.
    // options.Limits.MaxResponseBufferSize = 65536; // descomente se necessário

    options.Limits.MaxRequestBodySize          = 8 * 1024;
    options.Limits.MaxRequestHeadersTotalSize  = 4 * 1024;
    options.Limits.MaxRequestLineSize          = 1 * 1024;
    options.Limits.MaxConcurrentUpgradedConnections = 0;

    // Com 250 VUs e 2 instâncias, cada uma recebe ~125 conexões simultâneas.
    // O limite padrão do Kestrel para MaxConcurrentConnections é ilimitado,
    // mas podemos ajudar o scheduler com limites explícitos.
    // options.Limits.MaxConcurrentConnections = 200; // descomente e ajuste

    // KeepAlive longo mantém conexões abertas (bom para k6 que reutiliza conexão)
    options.Limits.KeepAliveTimeout      = TimeSpan.FromMinutes(5);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10); // era 5s — muito agressivo sob carga

    if (!string.IsNullOrWhiteSpace(udsPath))
    {
        var udsDir = Path.GetDirectoryName(udsPath);
        if (!string.IsNullOrWhiteSpace(udsDir))
            Directory.CreateDirectory(udsDir);

        if (File.Exists(udsPath))
            File.Delete(udsPath);

        // UDS tem latência menor que TCP loopback — ótima escolha com nginx/envoy
        options.ListenUnixSocket(udsPath, listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http1;
        });
    }
    else
    {
        options.ListenAnyIP(9999, listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http1;
        });
    }
});

// ── Configurações de domínio ─────────────────────────────────────────────────
var resourcesPath = Environment.GetEnvironmentVariable("RESOURCES_PATH")
    ?? Path.Combine(builder.Environment.ContentRootPath, "resources");
var indexPath = Environment.GetEnvironmentVariable("INDEX_PATH")
    ?? Path.Combine(builder.Environment.ContentRootPath, "data", "knn.idx");
var knnK = ParseEnvInt("KNN_K", 5);
var fraudThreshold = float.TryParse(
    Environment.GetEnvironmentVariable("FRAUD_THRESHOLD"),
    NumberStyles.Float,
    CultureInfo.InvariantCulture,
    out var fraudThresholdValue)
    ? fraudThresholdValue
    : 0.6f;
var maxPartitionsToScan = ParseEnvInt("MAX_PARTITIONS_TO_SCAN", 2);
var maxPartitionItems   = ParseEnvInt("MAX_PARTITION_ITEMS",    0);
var hardPartitionLimit  = EnvBool("MAX_PARTITION_HARD");
var partitionOnly       = EnvBool("PARTITION_ONLY");
var useIvf              = EnvBool("USE_IVF");
var ivfClusters         = ParseEnvInt("IVF_CLUSTERS",     256);
var ivfNProbe           = ParseEnvInt("IVF_NPROBE",         1);
var ivfMaxVectors       = ParseEnvInt("IVF_MAX_VECTORS",    0);
var ivfIndexPath        = Environment.GetEnvironmentVariable("IVF_INDEX_PATH")
    ?? Path.Combine(builder.Environment.ContentRootPath, "data", "ivf.idx");

// ── DI ───────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IResourceProvider>(sp =>
    new JsonResourceProvider(resourcesPath, sp.GetService<ILogger<JsonResourceProvider>>()));
builder.Services.AddSingleton<IAppReadiness, AppReadiness>();
builder.Services.AddSingleton<IVectorizer, DefaultVectorizer>();
builder.Services.AddSingleton(new FraudDetectionOptions
{
    KnnK           = knnK,
    FraudThreshold = fraudThreshold
});

var useBrute = EnvBool("USE_BRUTE");
if (useIvf)
{
    builder.Services.AddSingleton(new IvfIndexOptions
    {
        IndexPath            = ivfIndexPath,
        ClusterCount         = ivfClusters,
        NProbe               = ivfNProbe,
        MaxVectorsPerCluster = ivfMaxVectors
    });
    builder.Services.AddSingleton<IvfIndexSearch>();
    builder.Services.AddSingleton<IVectorIndex>(sp => sp.GetRequiredService<IvfIndexSearch>());
    builder.Services.AddSingleton<IVectorSearch>(sp => sp.GetRequiredService<IvfIndexSearch>());
}
else if (useBrute)
{
    builder.Services.AddSingleton(new BruteForceIndexOptions
    {
        IndexPath           = indexPath,
        MaxPartitionsToScan = maxPartitionsToScan,
        MaxPartitionItems   = maxPartitionItems,
        HardPartitionLimit  = hardPartitionLimit,
        PartitionOnly       = partitionOnly
    });
    builder.Services.AddSingleton<BruteForceIndexSearch>();
    builder.Services.AddSingleton<IVectorIndex>(sp => sp.GetRequiredService<BruteForceIndexSearch>());
    builder.Services.AddSingleton<IVectorSearch>(sp => sp.GetRequiredService<BruteForceIndexSearch>());
}
else
{
    builder.Services.AddSingleton(new MmapIndexOptions
    {
        IndexPath           = indexPath,
        MaxPartitionsToScan = maxPartitionsToScan,
        MaxPartitionItems   = maxPartitionItems,
        HardPartitionLimit  = hardPartitionLimit,
        PartitionOnly       = partitionOnly
    });
    builder.Services.AddSingleton<MmapIndexSearch>();
    builder.Services.AddSingleton<IVectorIndex>(sp => sp.GetRequiredService<MmapIndexSearch>());
    builder.Services.AddSingleton<IVectorSearch>(sp => sp.GetRequiredService<MmapIndexSearch>());
}

builder.Services.AddSingleton<DetectFraudUseCase>();
builder.Services.AddSingleton<FastFraudProcessor>();
builder.Services.AddSingleton<FraudResponseCache>();
builder.Services.AddHostedService<IndexWarmupService>();

// ── Pipeline ─────────────────────────────────────────────────────────────────
var app = builder.Build();

// Desabilitar logging de requests do ASP.NET Core em produção
// (redundante com o filtro acima, mas garante)
app.UseWhen(_ => false, _ => { }); // no-op, só para documentar intenção

if (!string.IsNullOrWhiteSpace(udsPath) && OperatingSystem.IsLinux())
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try
        {
            File.SetUnixFileMode(
                udsPath,
                UnixFileMode.UserRead  | UnixFileMode.UserWrite  |
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite  |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Failed to set UDS socket permissions.");
        }
    });
}

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapGet("/ready", ([FromServices] IAppReadiness readiness) =>
    readiness.IsReady ? Results.Ok() : Results.StatusCode(503));

app.MapPost("/fraud-score", async (
    HttpContext context,
    [FromServices] FastFraudProcessor processor,
    [FromServices] FraudResponseCache responseCache,
    CancellationToken cancellationToken) =>
{
    var pipe          = context.Request.BodyReader;
    var contentLength = context.Request.ContentLength;

    // Leitura do body via pipe (zero-copy até o ToArray)
    // Otimização: se ContentLength conhecido, espera exatamente aquela quantidade
    // antes de processar — evita reads parciais e loops desnecessários.
    ReadResult readResult;
    while (true)
    {
        readResult = await pipe.ReadAsync(cancellationToken);
        var current = readResult.Buffer;

        if (contentLength is null)
        {
            if (readResult.IsCompleted) break;
        }
        else if (current.Length >= contentLength.Value || readResult.IsCompleted)
        {
            break;
        }

        pipe.AdvanceTo(current.Start, current.End);
    }

    var buffer    = readResult.Buffer;
    var bodyBytes = buffer.ToArray();
    pipe.AdvanceTo(buffer.End);

    var score = processor.Score(bodyBytes);

    var payload  = responseCache.BodyForScore(score);
    var response = context.Response;
    response.StatusCode     = 200;
    response.ContentType    = "application/json";
    response.ContentLength  = payload.Length;
    response.Headers.Date   = default; // elimina overhead de formatação de data

    response.BodyWriter.Write(payload.Span);

    var flush = response.BodyWriter.FlushAsync(cancellationToken);
    if (!flush.IsCompletedSuccessfully)
        await flush;
});

app.Run();

// ── Helpers ───────────────────────────────────────────────────────────────────
static int ParseEnvInt(string key, int fallback)
{
    return int.TryParse(Environment.GetEnvironmentVariable(key), out var v) && v > 0
        ? v
        : fallback;
}

static bool EnvBool(string key)
    => (Environment.GetEnvironmentVariable(key) ?? "false")
        .Equals("true", StringComparison.OrdinalIgnoreCase);