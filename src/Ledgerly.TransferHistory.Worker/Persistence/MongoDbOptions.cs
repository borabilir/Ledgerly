namespace Ledgerly.TransferHistory.Worker.Persistence;

internal sealed class MongoDbOptions
{
    public const string SectionName = "MongoDb";

    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = "ledgerly_transfer_history";
}
