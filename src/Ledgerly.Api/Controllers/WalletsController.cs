using Ledgerly.Api.Contracts.Wallets;
using Ledgerly.Application.Wallets.CreateWallet;
using Ledgerly.Application.Wallets.GetWallet;
using Microsoft.AspNetCore.Mvc;

namespace Ledgerly.Api.Controllers;

[ApiController]
[Route("api/wallets")]
public sealed class WalletsController : ControllerBase
{
    private readonly CreateWalletHandler _createWalletHandler;
    private readonly GetWalletHandler _getWalletHandler;

    public WalletsController(
        CreateWalletHandler createWalletHandler,
        GetWalletHandler getWalletHandler
    )
    {
        _createWalletHandler = createWalletHandler;
        _getWalletHandler = getWalletHandler;
    }

    [HttpPost]
    [ProducesResponseType<CreateWalletResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CreateWalletResponse>> Create(
        CreateWalletRequest request,
        CancellationToken cancellationToken
    )
    {
        var command = new CreateWalletCommand(request.OwnerId, request.CurrencyCode);
        var result = await _createWalletHandler.Handle(command, cancellationToken);
        var response = new CreateWalletResponse(result.WalletId);

        return CreatedAtAction(nameof(GetById), new { walletId = result.WalletId }, response);
    }

    [HttpGet("{walletId:guid}")]
    [ProducesResponseType<GetWalletResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetWalletResponse>> GetById(
        Guid walletId,
        CancellationToken cancellationToken
    )
    {
        var result = await _getWalletHandler.Handle(new GetWalletQuery(walletId), cancellationToken);

        if (result is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Wallet not found",
                detail: $"Wallet '{walletId}' was not found."
            );
        }

        return Ok(new GetWalletResponse(
            result.WalletId,
            result.OwnerId,
            result.CurrencyCode,
            result.Status.ToString(),
            result.Balance,
            result.CreatedAtUtc
        ));
    }
}
