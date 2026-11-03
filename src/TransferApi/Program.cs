using System.Text.Json.Serialization;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using TransferApi.Consumers;
using TransferApi.Data;
using TransferApi.Repositories;
using TransferApi.Repositories.IRepositories;
using TransferApi.Services;
using TransferApi.Services.IServices;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddDbContext<TransferDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("TransferDb"))
           .UseSnakeCaseNamingConvention());

builder.Services.AddScoped<ITransferRepository, TransferRepository>();
builder.Services.AddScoped<ITransferService, TransferService>();

builder.Services.AddMassTransit(x =>
{
    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("transfer-api", false));
    x.AddConsumer<TransferApprovedConsumer>();
    x.AddConsumer<TransferRejectedConsumer>();

    x.AddEntityFrameworkOutbox<TransferDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });

    x.AddConfigureEndpointsCallback((context, name, cfg) =>
    cfg.UseEntityFrameworkOutbox<TransferDbContext>(context));

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(new Uri(builder.Configuration.GetConnectionString("RabbitMq")!));
        cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
// Migrations are not applied here: they run as a separate step before the service starts
// (migration bundle in docker-compose and a Kubernetes Job; dotnet ef database update locally).

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Makes the auto-generated Program class public so integration tests can use WebApplicationFactory<Program>.
public partial class Program { }
