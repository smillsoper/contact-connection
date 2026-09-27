using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Payments;

/// <summary>
/// Talks to Authorize.Net's JSON API (createTransactionRequest). Credentials are resolved per-call
/// via the campaign -> client -> tenant cascade (Authorize.Net-specific key names — a different
/// gateway would have its own shape entirely, hence this lookup living here rather than in a shared
/// helper; see IPaymentGatewayClient's doc comment). Field-name/format mapping below is based on
/// Authorize.Net's public JSON API reference — verify against a real sandbox response during
/// live testing rather than trusting this blind, and adjust if anything doesn't match.
/// </summary>
public class AuthorizeNetGatewayClient(
    IHttpClientFactory httpClientFactory,
    ITenantCredentialStore credentials,
    ILogger<AuthorizeNetGatewayClient> logger) : IPaymentGatewayClient
{
    public string ProviderKey => "authorize_net";

    private const string SandboxUrl    = "https://apitest.authorize.net/xml/v1/request.api";
    private const string ProductionUrl = "https://api.authorize.net/xml/v1/request.api";

    public async Task<GatewayAuthResult> AuthorizeAsync(
        Guid campaignId, Guid clientId, decimal amount,
        string cardNumber, string expirationMMYY, string cvv, string? zip, string? orderNumber,
        CancellationToken ct = default)
    {
        var (apiLoginId, transactionKey, url) = await ResolveCredentialsAsync(campaignId, clientId, ct);
        if (apiLoginId is null || transactionKey is null)
            return new GatewayAuthResult(false, PaymentTransactionStatus.Error, null, null, null,
                "No Authorize.Net credentials configured for this campaign/client/tenant.", null, null, null, null);

        var body = BuildAuthOnlyRequest(apiLoginId, transactionKey, amount, cardNumber, expirationMMYY, cvv, zip, orderNumber);

        var client = httpClientFactory.CreateClient("AuthorizeNet");
        JsonNode? json;
        try
        {
            var response = await client.PostAsJsonAsync(url, body, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);
            json = JsonNode.Parse(StripBom(raw));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Authorize.Net authorize call failed (network/transport error).");
            return new GatewayAuthResult(false, PaymentTransactionStatus.Error, null, null, null,
                $"Gateway request failed: {ex.Message}", null, null, null, null);
        }

        return ParseAuthResponse(json, cardNumber);
    }

    public async Task<GatewayVoidResult> VoidAsync(
        Guid campaignId, Guid clientId, string gatewayTransactionId, CancellationToken ct = default)
    {
        var (apiLoginId, transactionKey, url) = await ResolveCredentialsAsync(campaignId, clientId, ct);
        if (apiLoginId is null || transactionKey is null)
            return new GatewayVoidResult(false, "No Authorize.Net credentials configured for this campaign/client/tenant.");

        var body = new
        {
            createTransactionRequest = new
            {
                merchantAuthentication = new { name = apiLoginId, transactionKey },
                transactionRequest = new
                {
                    transactionType = "voidTransaction",
                    refTransId = gatewayTransactionId,
                },
            },
        };

        var client = httpClientFactory.CreateClient("AuthorizeNet");
        JsonNode? json;
        try
        {
            var response = await client.PostAsJsonAsync(url, body, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);
            json = JsonNode.Parse(StripBom(raw));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Authorize.Net void call failed (network/transport error).");
            return new GatewayVoidResult(false, $"Gateway request failed: {ex.Message}");
        }

        var responseCode = json?["transactionResponse"]?["responseCode"]?.GetValue<string>();
        if (responseCode == "1")
            return new GatewayVoidResult(true, null);

        var reason = ExtractReason(json);
        return new GatewayVoidResult(false, reason ?? "Void was not approved by the gateway.");
    }

    /// <summary>
    /// Builds the createTransactionRequest body. Authorize.Net's JSON API is backed by its XML
    /// schema, so element ORDER matters (an out-of-order element is rejected) — refId comes after
    /// merchantAuthentication, and within transactionRequest: transactionType, amount, payment,
    /// order, ..., billTo. Optional elements are omitted entirely rather than sent as null.
    ///
    /// The order number goes in both refId (echoed back in the response) and order.invoiceNumber.
    /// invoiceNumber is part of Authorize.Net's duplicate-transaction check (same card + amount +
    /// invoice number + bill-to within the duplicate window is rejected), so distinct orders are no
    /// longer mistaken for duplicates, while a genuine resubmission of the same order still is.
    /// refId/poNumber are NOT part of that check — which is why CRMPro's poNumber-only approach
    /// didn't help. Both fields cap at 20 chars (OrderNumberSequence enforces that).
    /// </summary>
    internal static JsonObject BuildAuthOnlyRequest(
        string apiLoginId, string transactionKey, decimal amount,
        string cardNumber, string expirationMMYY, string cvv, string? zip, string? orderNumber)
    {
        var transactionRequest = new JsonObject
        {
            ["transactionType"] = "authOnlyTransaction",
            ["amount"] = amount.ToString("F2"),
            ["payment"] = new JsonObject
            {
                ["creditCard"] = new JsonObject
                {
                    ["cardNumber"] = cardNumber,
                    ["expirationDate"] = ToExpirationDate(expirationMMYY),
                    ["cardCode"] = cvv,
                },
            },
        };
        if (!string.IsNullOrEmpty(orderNumber))
            transactionRequest["order"] = new JsonObject { ["invoiceNumber"] = orderNumber };
        if (!string.IsNullOrEmpty(zip))
            transactionRequest["billTo"] = new JsonObject { ["zip"] = zip };

        var request = new JsonObject
        {
            ["merchantAuthentication"] = new JsonObject { ["name"] = apiLoginId, ["transactionKey"] = transactionKey },
        };
        if (!string.IsNullOrEmpty(orderNumber))
            request["refId"] = orderNumber;
        request["transactionRequest"] = transactionRequest;

        return new JsonObject { ["createTransactionRequest"] = request };
    }

    private GatewayAuthResult ParseAuthResponse(JsonNode? json, string cardNumber)
    {
        var txResponse = json?["transactionResponse"];
        var responseCode = txResponse?["responseCode"]?.GetValue<string>();
        var last4 = cardNumber.Length >= 4 ? cardNumber[^4..] : null;
        var cardType = txResponse?["accountType"]?.GetValue<string>();

        var status = responseCode switch
        {
            "1" => PaymentTransactionStatus.Approved,
            "2" => PaymentTransactionStatus.Declined,
            _   => PaymentTransactionStatus.Error, // 3 (error), 4 (held for review), or unrecognized
        };

        var reason = ExtractReason(json);

        return new GatewayAuthResult(
            Succeeded: status == PaymentTransactionStatus.Approved,
            Status: status,
            GatewayTransactionId: txResponse?["transId"]?.GetValue<string>(),
            AuthCode: txResponse?["authCode"]?.GetValue<string>(),
            ResponseCode: responseCode,
            ResponseReasonText: reason,
            AvsResultCode: txResponse?["avsResultCode"]?.GetValue<string>(),
            CvvResultCode: txResponse?["cvvResultCode"]?.GetValue<string>(),
            CardLast4: last4,
            CardType: cardType);
    }

    /// <summary>Authorize.Net's JSON API nests the actual result message in different places
    /// depending on outcome — a transaction-level messages[]/errors[] array under
    /// transactionResponse for an approved/declined card decision, or the top-level messages.message[]
    /// / a top-level "Error" resultCode for a request-level failure (bad credentials, malformed
    /// request). Try each in order and fall back gracefully.</summary>
    private static string? ExtractReason(JsonNode? json)
    {
        var txMessages = json?["transactionResponse"]?["messages"]?.AsArray();
        if (txMessages?.Count > 0)
            return txMessages[0]?["description"]?.GetValue<string>();

        var txErrors = json?["transactionResponse"]?["errors"]?.AsArray();
        if (txErrors?.Count > 0)
            return txErrors[0]?["errorText"]?.GetValue<string>();

        var topMessages = json?["messages"]?["message"]?.AsArray();
        if (topMessages?.Count > 0)
            return topMessages[0]?["text"]?.GetValue<string>();

        return null;
    }

    /// <summary>Converts the platform's stored MMYY expiry (see tf_secure_collect's expiry_mmyy
    /// validation) to Authorize.Net's documented "YYYY-MM" format.</summary>
    private static string ToExpirationDate(string mmyy)
    {
        if (mmyy.Length != 4) return mmyy; // pass through unchanged if it's not the expected shape
        var month = mmyy[..2];
        var year = "20" + mmyy[2..];
        return $"{year}-{month}";
    }

    private static string StripBom(string s) => s.TrimStart('﻿');

    private async Task<(string? apiLoginId, string? transactionKey, string url)> ResolveCredentialsAsync(
        Guid campaignId, Guid clientId, CancellationToken ct)
    {
        var apiLoginId = await ResolveScopedAsync("ApiLoginId", campaignId, clientId, ct);
        var transactionKey = await ResolveScopedAsync("TransactionKey", campaignId, clientId, ct);
        var environment = await ResolveScopedAsync("Environment", campaignId, clientId, ct) ?? "sandbox";
        var url = environment.Equals("production", StringComparison.OrdinalIgnoreCase) ? ProductionUrl : SandboxUrl;
        return (apiLoginId, transactionKey, url);
    }

    /// <summary>Campaign -> client -> tenant credential cascade — same precedence
    /// CustomFieldDefinition already uses for scope resolution, applied here to gateway credentials
    /// since Life Seasons (and presumably other multi-campaign clients) run a separate Authorize.Net
    /// merchant account per campaign, not just per client.</summary>
    private async Task<string?> ResolveScopedAsync(string field, Guid campaignId, Guid clientId, CancellationToken ct)
    {
        if (campaignId != Guid.Empty)
        {
            var campaignValue = await credentials.GetAsync($"AuthorizeNet:{campaignId}:{field}", ct);
            if (campaignValue is not null) return campaignValue;
        }

        if (clientId != Guid.Empty)
        {
            var clientValue = await credentials.GetAsync($"AuthorizeNet:{clientId}:{field}", ct);
            if (clientValue is not null) return clientValue;
        }

        return await credentials.GetAsync($"AuthorizeNet:{field}", ct);
    }
}
