using Ledgerly.Api.Contracts.Transfers;
using Ledgerly.Application.Wallets.TransferWallet;
using Microsoft.AspNetCore.Mvc;

namespace Ledgerly.Api.Controllers;

[ApiController]
[Route("api/transfers")]
public sealed class TransfersController(TransferWalletHandler handler) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CreateTransferResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CreateTransferResponse>> Create(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CreateTransferRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new TransferWalletCommand(
            request.SourceWalletId,
            request.DestinationWalletId,
            request.Amount,
            idempotencyKey ?? string.Empty), cancellationToken);
        if (result is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Wallet not found",
                detail: "The source or destination wallet was not found.");
        }

        return Ok(new CreateTransferResponse(
            result.TransferId,
            result.JournalEntryId,
            result.SourceWalletId,
            result.DestinationWalletId,
            result.CurrencyCode,
            result.Amount,
            result.SourceBalance,
            result.DestinationBalance));
    }
}
