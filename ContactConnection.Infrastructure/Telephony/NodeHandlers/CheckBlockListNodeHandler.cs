using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

public class CheckBlockListNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_check_block_list";

    private readonly ITenantDbContextFactory _factory;

    public CheckBlockListNodeHandler(ITenantDbContextFactory factory) => _factory = factory;

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        await using var db = _factory.Create(ctx.TenantSchemaName);
        var now = DateTimeOffset.UtcNow;

        var entries = await db.BlockListEntries
            .Where(e => e.TenantId == ctx.TenantId && (e.ExpiresAt == null || e.ExpiresAt > now))
            .Select(e => new { e.PhoneNumber, e.MatchType })
            .ToListAsync(ct);

        // Use checkVariable if set; otherwise fall back to the call's ANI. Accepts a bare
        // variable name or a {{flow.x}}/{{shared.x}}/{{caller.ani}} template — same resolution
        // every other value field in the telephony designer uses (see
        // TelSetVariableNodeHandler.ResolveNameOrTemplate).
        var checkVariable = node["checkVariable"]?.GetValue<string>();
        var resolvedCheck = TelSetVariableNodeHandler.ResolveNameOrTemplate(checkVariable, ctx);
        var numberToCheck = !string.IsNullOrWhiteSpace(checkVariable) && !string.IsNullOrEmpty(resolvedCheck)
            ? resolvedCheck
            : ctx.CallerNumber;

        var normalizedCheck = NormalizeNumber(numberToCheck);
        var blocked = entries.Any(e =>
            e.MatchType == BlockListMatchType.Exact
                ? NormalizeNumber(e.PhoneNumber) == normalizedCheck
                : normalizedCheck.StartsWith(NormalizeNumber(e.PhoneNumber), StringComparison.Ordinal));

        var transition = blocked ? "blocked" : "not_blocked";
        var nextNodeId = node["transitions"]?[transition]?.GetValue<string>()
                      ?? node["transitions"]?["default"]?.GetValue<string>();

        return new TelephonyNodeResult(nextNodeId, transition);
    }

    /// <summary>
    /// Reduces a phone number to bare digits and drops a leading US country code
    /// ("1" + 10 digits) so entries and ANI can be entered/stored in whatever form
    /// (e.g. "+15551234567", "15551234567", "5551234567") and still compare equal —
    /// mirrors the destination-number normalization in EslBackgroundService's DID routing.
    /// </summary>
    private static string NormalizeNumber(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length == 11 && digits[0] == '1' ? digits[1..] : digits;
    }
}
