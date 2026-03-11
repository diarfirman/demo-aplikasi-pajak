using Elastic.Apm.NetCoreAll;
using TaxApi.Endpoints;
using TaxApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Elastic APM - auto-instruments HTTP requests, DB, etc.
builder.Services.AddAllElasticApm();

// Services
builder.Services.AddSingleton<ElasticsearchService>();
builder.Services.AddSingleton<RabbitMqService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RabbitMqService>());

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Aplikasi Pajak API", Version = "v1", Description = "API Pelaporan Pajak Sederhana" });
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Aplikasi Pajak API v1");
    c.RoutePrefix = "swagger";
});

// Map endpoints
app.MapTaxPayerEndpoints();
app.MapCalculationEndpoints();
app.MapReportEndpoints();
app.MapNotificationEndpoints();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow })).WithTags("Health");

app.Run();
