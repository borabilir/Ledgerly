using System.ComponentModel.DataAnnotations;

namespace Ledgerly.Api.Contracts.Wallets;

public sealed record CreateWalletRequest(
    Guid OwnerId,
    [param: Required, StringLength(3, MinimumLength = 3)] string CurrencyCode
);
