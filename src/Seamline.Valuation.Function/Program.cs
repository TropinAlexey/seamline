using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Seamline.Modules.MarketData.Internal;
using Seamline.Modules.Reference.Internal;
using Seamline.Modules.Risk.Internal;
using Seamline.SharedKernel;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("seamline-valuation-function"))
    .WithTracing(t => t
        .AddHttpClientInstrumentation()
        .AddSource("Npgsql")
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddHttpClientInstrumentation()
        .AddMeter("Npgsql")
        .AddOtlpExporter());

builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
builder.Services.AddScoped<TenantSessionVariableInterceptor>();

builder.Services.AddReferenceModule(builder.Configuration);
builder.Services.AddRiskModule(builder.Configuration);
builder.Services.AddMarketDataModule(builder.Configuration);

var host = builder.Build();

await host.Services.MigrateReferenceModuleAsync();
await host.Services.MigrateRiskModuleAsync();
await host.Services.MigrateMarketDataModuleAsync();

await host.RunAsync();
