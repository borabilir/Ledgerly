using Ledgerly.TransferHistory.Worker.Persistence;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<MongoDbOptions>()
    .Bind(builder.Configuration.GetSection(MongoDbOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.ConnectionString),
        "MongoDb:ConnectionString must be configured.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.DatabaseName),
        "MongoDb:DatabaseName must be configured.")
    .ValidateOnStart();

builder.Services.AddSingleton<IMongoClient>(services =>
{
    var options = services.GetRequiredService<IOptions<MongoDbOptions>>().Value;
    return new MongoClient(options.ConnectionString);
});
builder.Services.AddSingleton(services =>
{
    var options = services.GetRequiredService<IOptions<MongoDbOptions>>().Value;
    var client = services.GetRequiredService<IMongoClient>();
    return client.GetDatabase(options.DatabaseName);
});
builder.Services.AddHostedService<MongoDbStartupCheck>();

var host = builder.Build();
await host.RunAsync();
