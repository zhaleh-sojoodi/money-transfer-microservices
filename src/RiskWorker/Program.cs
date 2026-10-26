using MassTransit;
using RiskWorker.Consumers;
using RiskWorker.Services;
using RiskWorker.Services.IServices;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddScoped<IRiskEvaluator, RiskEvaluator>();

builder.Services.AddMassTransit(x =>
{
    // Queue names in kebab-case: TransferRequestedConsumer -> "transfer-requested".
    x.SetKebabCaseEndpointNameFormatter();

    x.AddConsumer<TransferRequestedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(new Uri(builder.Configuration.GetConnectionString("RabbitMq")!));

        // If Consume throws: retry 3 times, 5 seconds apart, then move the message to "transfer-requested_error".
        cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));

        // Hold messages published inside Consume until it finishes successfully,
        // so a failed attempt (that will be retried) never publishes anything.
        cfg.UseInMemoryOutbox(context);

        // Creates the queue for each consumer and binds it to the message type's exchange.
        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();
host.Run();
