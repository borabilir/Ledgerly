using Ledgerly.Application.Wallets.CreateWallet;
using Ledgerly.Application.Wallets.GetWallet;
using Ledgerly.Application.Wallets.TestDeposit;
using Ledgerly.Application.Wallets.TransferWallet;
using Ledgerly.Api.Errors;
using Ledgerly.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddScoped<CreateWalletHandler>();
builder.Services.AddScoped<GetWalletHandler>();
builder.Services.AddScoped<TestDepositHandler>();
builder.Services.AddScoped<TransferWalletHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("IntegrationTests"))
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
