using ContactConnection.Infrastructure.Extensions;
using ContactConnection.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Azure Key Vault — same conditional wiring as ContactConnection.Api's Program.cs, with
// the same non-fatal fallback if the vault is unreachable (ConfigurationManager connects
// eagerly the moment a source is added, so a stale/unreachable credential must not crash
// startup — see the longer comment there).
var vaultUri = builder.Configuration["KeyVault:VaultUri"];
if (!string.IsNullOrWhiteSpace(vaultUri))
{
    try
    {
        builder.Configuration.AddAzureKeyVault(new Uri(vaultUri), AzureCredentialFactory.Resolve(builder.Configuration));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            $"WARNING: KeyVault:VaultUri is set ({vaultUri}) but Key Vault could not be reached — " +
            $"continuing without it, using existing configuration sources instead. Error: {ex.Message}");
    }
}

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<SubscriptionProcessingService>();
builder.Services.AddHostedService<FreeSwitchEslService>();
builder.Services.AddHostedService<RecordingMergeService>();
builder.Services.AddHostedService<CallbackProcessingService>();

var host = builder.Build();
host.Run();
