using Ledgerly.Application.Wallets.CreateWallet;
using Ledgerly.Application.Ledger;
using Ledgerly.Application.Wallets.TestDeposit;
using Ledgerly.Application.Wallets.TransferWallet;
using Ledgerly.Domain.Wallets;
using Microsoft.AspNetCore.Diagnostics;

namespace Ledgerly.Api.Errors;

internal sealed class ApiExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(ILogger<ApiExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        var (statusCode, title, detail) = exception switch
        {
            WalletAlreadyExistsException => (
                StatusCodes.Status409Conflict,
                "Wallet already exists",
                exception.Message
            ),
            LedgerWriteConflictException => (
                StatusCodes.Status409Conflict,
                "Ledger write conflict",
                exception.Message
            ),
            TestDepositIdempotencyConflictException => (
                StatusCodes.Status409Conflict,
                "Idempotency key conflict",
                exception.Message
            ),
            TestDepositIdempotencyWriteConflictException => (
                StatusCodes.Status409Conflict,
                "Idempotency request in progress",
                "Retry with the same idempotency key."
            ),
            TransferIdempotencyConflictException => (
                StatusCodes.Status409Conflict,
                "Transfer idempotency key conflict",
                exception.Message
            ),
            TransferIdempotencyWriteConflictException => (
                StatusCodes.Status409Conflict,
                "Transfer request in progress",
                "Retry with the same idempotency key."
            ),
            InsufficientFundsException => (
                StatusCodes.Status409Conflict,
                "Insufficient funds",
                exception.Message
            ),
            SameWalletTransferException or TransferCurrencyMismatchException => (
                StatusCodes.Status400BadRequest,
                "Invalid transfer",
                exception.Message
            ),
            ArgumentException => (
                StatusCodes.Status400BadRequest,
                "Invalid request",
                exception.Message
            ),
            _ => (
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred",
                "The server could not process the request."
            ),
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception while processing the request.");
        }

        await Results
            .Problem(statusCode: statusCode, title: title, detail: detail)
            .ExecuteAsync(httpContext);

        return true;
    }
}
