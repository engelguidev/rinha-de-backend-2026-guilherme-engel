using Microsoft.AspNetCore.Mvc;
using Rinha.FraudDetection.Application.Interfaces;
using Rinha.FraudDetection.Application.UseCases;
using Rinha.FraudDetection.Infrastructure.Resources;
using Rinha.FraudDetection.Infrastructure.Startup;
using Rinha.FraudDetection.Infrastructure.Vectorization;
using Rinha.FraudDetection.Presentation.Contracts;
using Rinha.FraudDetection.Infrastructure.Index;

var builder = WebApplication.CreateBuilder(args);

var resourcesPath = Environment.GetEnvironmentVariable("RESOURCES_PATH")
    ?? Path.Combine(builder.Environment.ContentRootPath, "resources");
var indexPath = Environment.GetEnvironmentVariable("INDEX_PATH")
    ?? Path.Combine(builder.Environment.ContentRootPath, "data", "knn.idx");

builder.Services.AddSingleton<IResourceProvider>(_ => new JsonResourceProvider(resourcesPath));
builder.Services.AddSingleton<IAppReadiness, AppReadiness>();
builder.Services.AddSingleton<IVectorizer, DefaultVectorizer>();
builder.Services.AddSingleton(new MmapIndexOptions { IndexPath = indexPath });
builder.Services.AddSingleton<MmapIndexSearch>();
builder.Services.AddSingleton<IVectorIndex>(sp => sp.GetRequiredService<MmapIndexSearch>());
builder.Services.AddSingleton<IVectorSearch>(sp => sp.GetRequiredService<MmapIndexSearch>());
builder.Services.AddSingleton<DetectFraudUseCase>();
builder.Services.AddHostedService<IndexWarmupService>();

var app = builder.Build();

app.MapGet("/ready", ([FromServices] IAppReadiness readiness) =>
    readiness.IsReady ? Results.Ok() : Results.StatusCode(503));

app.MapPost("/fraud-score", async (
    [FromBody] FraudScoreRequest request,
    [FromServices] DetectFraudUseCase useCase,
    [FromServices] IAppReadiness readiness,
    CancellationToken cancellationToken) =>
{
    if (!readiness.IsReady)
    {
        return Results.StatusCode(503);
    }

    var decision = await useCase.ExecuteAsync(request.ToDomain(), cancellationToken);
    return Results.Json(new FraudScoreResponse(decision.Approved, decision.FraudScore));
});

app.Run("http://0.0.0.0:9999");
