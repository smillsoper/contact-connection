using ContactConnection.Application.Interfaces.Services;
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

// No-op registrations for the SignalR-backed notifier interfaces + IEslCommanderFactory that
// ContactConnection.Api's Program.cs supplies with real implementations. AddInfrastructure()
// registers services (FlowEngine, TelephonyFlowEngine, AgentStateStore, CallStateHistoryRecorder,
// CallTraceRecorder, CallRecordingController) that depend on these; without a registration here,
// Host.CreateApplicationBuilder's ValidateOnBuild (on by default in Development) fails at startup
// even though none of the Worker's own hosted services resolve them. See NoOpNotifiers.cs and
// project memory project_worker_dev_boot.
builder.Services.AddScoped<IFlowNotifier, NoOpFlowNotifier>();
builder.Services.AddScoped<ICallTraceNotifier, NoOpCallTraceNotifier>();
builder.Services.AddSingleton<IDashboardNotifier, NoOpDashboardNotifier>();
builder.Services.AddSingleton<IEslCommanderFactory, NoOpEslCommanderFactory>();

builder.Services.AddHostedService<SubscriptionProcessingService>();
builder.Services.AddHostedService<RecordingMergeService>();
builder.Services.AddHostedService<ScheduledCallbackProcessingService>();

// Historical note: a legacy FreeSwitchEslService (CHANNEL_PARK→create-CallRecord translator)
// once lived here. It predated ContactConnection.Api's EslBackgroundService, which now owns the
// entire inbound telephony lifecycle (routing, flow engine, call records, hangup finalization).
// Running both made every inbound call create TWO call records — the API's (real, flow-driven)
// and the Worker's (a bare stub) — orphaning one in a non-terminal state on the supervisor
// dashboard. It was unregistered in Session 119 and the dead class deleted in Session 120. The
// Worker's hosted services below open their own ESL connections as needed.

var host = builder.Build();
host.Run();
