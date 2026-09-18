using Ledgerly.Api.Contracts.Wallets;
using Ledgerly.Application.Wallets.TestDeposit;
using Microsoft.AspNetCore.Mvc;

namespace Ledgerly.Api.Controllers;

[ApiController]
[Route("api/wallets/{walletId:guid}/test-deposits")]
public sealed class TestDepositsController(TestDepositHandler handler, IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<TestDepositResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<TestDepositResponse>> Create(Guid walletId, TestDepositRequest request,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("IntegrationTests"))
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Endpoint not available");
        }

        var result = await handler.Handle(new TestDepositCommand(walletId, request.Amount), cancellationToken);
        if (result is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Wallet not found",
                detail: $"Wallet '{walletId}' was not found.");
        }

        return Ok(new TestDepositResponse(result.WalletId, result.JournalEntryId, result.CurrencyCode,
            result.Amount, result.Balance));
    }
}
