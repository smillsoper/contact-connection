using ContactConnection.Infrastructure.Payments;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Payments;

/// <summary>
/// Authorize.Net's JSON API is validated against its XML schema, so element order matters and
/// nulls aren't tolerated everywhere — these pin the exact shape of the auth-only request.
/// </summary>
public class AuthorizeNetRequestTests
{
    private static List<string> Keys(System.Text.Json.Nodes.JsonNode? node)
        => node!.AsObject().Select(kv => kv.Key).ToList();

    [Fact]
    public void WithOrderNumber_SendsRefIdAndInvoiceNumber_InSchemaOrder()
    {
        var body = AuthorizeNetGatewayClient.BuildAuthOnlyRequest(
            "login", "key", 53.07m, "4111111111111111", "1227", "123", "97470", "LIFSEA-10000123");

        var request = body["createTransactionRequest"];
        Assert.Equal(["merchantAuthentication", "refId", "transactionRequest"], Keys(request));
        Assert.Equal("LIFSEA-10000123", request!["refId"]!.GetValue<string>());

        var tx = request["transactionRequest"];
        Assert.Equal(["transactionType", "amount", "payment", "order", "billTo"], Keys(tx));
        Assert.Equal("LIFSEA-10000123", tx!["order"]!["invoiceNumber"]!.GetValue<string>());
        Assert.Equal("53.07", tx["amount"]!.GetValue<string>());
        Assert.Equal("2027-12", tx["payment"]!["creditCard"]!["expirationDate"]!.GetValue<string>());
        Assert.Equal("97470", tx["billTo"]!["zip"]!.GetValue<string>());
    }

    [Fact]
    public void WithoutOrderNumberOrZip_OmitsThoseElementsEntirely()
    {
        var body = AuthorizeNetGatewayClient.BuildAuthOnlyRequest(
            "login", "key", 10m, "4111111111111111", "1227", "123", zip: null, orderNumber: null);

        var request = body["createTransactionRequest"];
        Assert.Equal(["merchantAuthentication", "transactionRequest"], Keys(request));
        Assert.Equal(["transactionType", "amount", "payment"], Keys(request!["transactionRequest"]));
        Assert.DoesNotContain("null", body.ToJsonString());
    }
}
