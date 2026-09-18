using Ledgerly.Application.Wallets.CreateWallet;
using Ledgerly.Application.Ledger;
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
