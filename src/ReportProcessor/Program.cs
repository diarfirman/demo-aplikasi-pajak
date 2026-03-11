using Elastic.Apm;
using Elastic.Apm.Extensions.Hosting;
using ReportProcessor;
using ReportProcessor.Services;

var builder = Host.CreateApplicationBuilder(args);

// Elastic APM - instrument Worker Service
builder.Services.AddElasticApm();

builder.Services.AddSingleton<RabbitMqConsumerService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
