using Ledgerly.Api.Contracts.Wallets;
using Ledgerly.Application.Wallets.CreateWallet;
using Microsoft.AspNetCore.Mvc;

namespace Ledgerly.Api.Controllers;

[ApiController]
[Route("api/wallets")]
public sealed class WalletsController : ControllerBase
{
    private readonly CreateWalletHandler _handler;

    public WalletsController(CreateWalletHandler handler)
    {
        _handler = handler;
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
        var result = await _handler.Handle(command, cancellationToken);
        var response = new CreateWalletResponse(result.WalletId);

        return StatusCode(StatusCodes.Status201Created, response);
    }
}
