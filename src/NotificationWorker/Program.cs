using MassTransit;
using Microsoft.EntityFrameworkCore;
using NotificationWorker.Consumers;
using NotificationWorker.Data;
using NotificationWorker.Repositories;
using NotificationWorker.Repositories.IRepositories;
using NotificationWorker.Services;
using NotificationWorker.Services.IServices;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("NotificationDb"))
           .UseSnakeCaseNamingConvention());

builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<INotificationServices, NotificationService>();

builder.Services.AddMassTransit(x =>
{
    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("notification-worker", false));

    x.AddConsumer<TransferApprovedConsumer>();
    x.AddConsumer<TransferRejectedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(new Uri(builder.Configuration.GetConnectionString("RabbitMq")!));

        cfg.UseMessageRetry(r =>
        {
            // Temporary problems (e.g. database briefly unavailable): retry 3 times, 5 seconds apart.
            r.Interval(3, TimeSpan.FromSeconds(5));

            // Invalid data fails the same way every time, so retrying is pointless:
            // send it straight to the "_error" queue.
            r.Ignore<ArgumentException>();
        });

        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();

if (host.Services.GetRequiredService<IHostEnvironment>().IsDevelopment())
{
    // Apply pending migrations on startup. Development only; production should migrate as a separate step.
    using var scope = host.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.Migrate();
}

host.Run();
