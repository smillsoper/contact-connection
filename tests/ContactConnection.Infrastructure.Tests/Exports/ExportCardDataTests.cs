using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects.Exports;
using ContactConnection.Infrastructure.Exports;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Exports;

/// <summary>S182 card-data exports: the card variables, masking, and the rules that keep card data out of other files.</summary>
public class ExportCardDataTests
{
    private static readonly Dictionary<string, string> Captured = new() { ["card_number"] = "4111111111111111", ["exp"] = "1230", ["cvv"] = "123", ["zip"] = "97470" };

    [Fact]
    public void Real_values_for_a_card_data_file()
    {
        var c = ExportCardData.Build(Captured, mask: false);
        Assert.Equal("4111111111111111", c["number"]);
        Assert.Equal("1111", c["last4"]);
        Assert.Equal("Visa", c["brand"]);
        Assert.Equal("12", c["exp_month"]);
        Assert.Equal("2030", c["exp_year"]);
        Assert.Equal("123", c["cvv"]);
        Assert.Equal("97470", c["zip"]);
    }

    [Fact]
    public void Preview_and_test_files_mask_everything_but_the_last_four()
    {
        var c = ExportCardData.Build(Captured, mask: true);
        Assert.Equal("XXXXXXXXXXXX1111", c["number"]);
        Assert.Equal("XXXX", c["expiration"]);
        Assert.Equal("XXX", c["cvv"]);
        var fields = (Dictionary<string, object?>)c["fields"]!;
        Assert.Equal("XXXXXXXXXXXX1111", fields["card_number"]);
        Assert.Equal("XXX", fields["cvv"]);
    }

    [Fact]
    public void Number_found_by_shape_when_the_key_is_custom()
    {
        Assert.Equal("5555555555554444", ExportCardData.Number(new Dictionary<string, string> { ["acct"] = "5555 5555 5555 4444" }));
        Assert.Equal("Mastercard", ExportCardData.Brand("5555555555554444"));
        Assert.Equal("American Express", ExportCardData.Brand("378282246310005"));
        Assert.False(ExportCardData.Luhn("4111111111111112"));
    }

    [Fact]
    public void Card_variables_need_the_card_data_switch_and_never_go_in_a_file_name()
    {
        var generator = new ExportGenerator(null!, null!);
        var spec = new ExportSpec { Columns = [new ExportColumn("PAN", "{{ card.number }}")] };
        Assert.Contains("Includes card data", generator.Validate(spec));
        Assert.Null(generator.Validate(spec with { IncludesCardData = true }));
        Assert.Contains("file name", generator.Validate(spec with { IncludesCardData = true, FileNameTemplate = "{{ card.last4 }}.csv" }));
        // Payment last-4 / type are ordinary order data, not the card variables.
        Assert.Null(generator.Validate(new ExportSpec { Columns = [new ExportColumn("Last4", "{{ call.payments[0].card_last4 }}")] }));
    }

    [Fact]
    public void Card_data_goes_only_by_ftps_with_pgp()
    {
        var ftpsPgp = new ExportDeliveryTarget { Name = "FC", Type = ExportDeliveryType.Ftps, Encryption = ExportEncryption.Pgp, PgpPublicKey = "-----BEGIN PGP" };
        Assert.Null(ftpsPgp.CardDataProblem());
        Assert.Contains("FTPS", (ftpsPgp with { Type = ExportDeliveryType.Sftp }).CardDataProblem());
        Assert.Contains("FTPS", (ftpsPgp with { Type = ExportDeliveryType.Email }).CardDataProblem());
        Assert.Contains("PGP", (ftpsPgp with { Encryption = ExportEncryption.Zip }).CardDataProblem());
        Assert.Contains("PGP", (ftpsPgp with { Encryption = ExportEncryption.None }).CardDataProblem());
    }

    [Fact]
    public void Until_exported_keeps_card_data_past_the_script()
    {
        Assert.True(CardDataRetentionMode.KeepsPastScript(CardDataRetentionMode.UntilExported));
        Assert.True(CardDataRetentionMode.KeepsPastScript(CardDataRetentionMode.UntilOrderSubmitted));
        Assert.False(CardDataRetentionMode.KeepsPastScript(CardDataRetentionMode.UntilScriptEnds));
    }
}
