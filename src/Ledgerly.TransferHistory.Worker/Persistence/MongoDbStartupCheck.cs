using MongoDB.Bson;
using MongoDB.Driver;

namespace Ledgerly.TransferHistory.Worker.Persistence;

internal sealed class MongoDbStartupCheck(
    IMongoDatabase database,
    ILogger<MongoDbStartupCheck> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await database.RunCommandAsync<BsonDocument>(
            new BsonDocument("ping", 1),
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Transfer History Worker connected to MongoDB database {DatabaseName}.",
            database.DatabaseNamespace.DatabaseName);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
