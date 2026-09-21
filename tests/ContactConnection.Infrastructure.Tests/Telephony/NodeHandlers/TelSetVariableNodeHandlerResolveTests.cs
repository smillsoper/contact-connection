using System.Text.RegularExpressions;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

/// <summary>
/// TelSetVariableNodeHandler.Resolve/ResolveKey is the shared `{{...}}` template resolver nearly
/// every other telephony node handler calls (tf_play's TTS text, tf_set_sip_header, tf_set_caller_id,
/// tf_branch's condition operand, etc.) — despite that reach, only the shared.*/caller.ani/call.did
/// paths had any coverage (see TelSetVariableNodeHandlerSharedVarsTests). This file covers the rest:
/// call.dnis alias, now.* namespace, flow. prefix stripping, plain-var lookup, unresolved keys,
/// multi-token interpolation, and the no-template short-circuit.
/// </summary>
public class TelSetVariableNodeHandlerResolveTests
{
    private static TelephonyFlowContext Ctx(string tenantTimezone = "America/Chicago") => new()
    {
        ChannelUuid       = "uuid-1",
        CallerNumber      = "+15551234567",
        DestinationNumber = "+15557654321",
        TenantId          = Guid.NewGuid(),
        CampaignId        = Guid.NewGuid(),
        CallRecordId      = Guid.NewGuid(),
        TenantSubdomain   = "test-tenant",
        TenantSchemaName  = "tenant_test_tenant",
        TenantTimezone    = tenantTimezone,
    };

    [Fact]
    public void NoTemplateMarker_ReturnsStringUnchanged()
    {
        var ctx = Ctx();
        Assert.Equal("plain text", TelSetVariableNodeHandler.Resolve("plain text", ctx));
        // Short-circuits on the presence of "{{" only — a lone "}}" with no opener is untouched.
        Assert.Equal("oops }} no opener", TelSetVariableNodeHandler.Resolve("oops }} no opener", ctx));
    }

    [Fact]
    public void CallDnis_AliasesDestinationNumber_SameAsCallDid()
    {
        var ctx = Ctx();
        Assert.Equal(ctx.DestinationNumber, TelSetVariableNodeHandler.Resolve("{{call.dnis}}", ctx));
    }

    [Fact]
    public void FlowPrefix_IsStripped_ThenLooksUpInVars()
    {
        var ctx = Ctx();
        ctx.Vars["entered_phone"] = "5416704541";
        Assert.Equal("5416704541", TelSetVariableNodeHandler.Resolve("{{flow.entered_phone}}", ctx));
    }

    [Fact]
    public void PlainKey_NoNamespace_LooksUpInVarsDirectly()
    {
        var ctx = Ctx();
        ctx.Vars["disposition"] = "sale";
        Assert.Equal("sale", TelSetVariableNodeHandler.Resolve("{{disposition}}", ctx));
    }

    [Fact]
    public void UnresolvedKey_ResolvesToEmptyString_NotLiteralTag()
    {
        var ctx = Ctx();
        Assert.Equal("", TelSetVariableNodeHandler.Resolve("{{never_set}}", ctx));
    }

    [Fact]
    public void KeyWithWhitespace_IsTrimmedBeforeLookup()
    {
        var ctx = Ctx();
        ctx.Vars["myVar"] = "hi";
        Assert.Equal("hi", TelSetVariableNodeHandler.Resolve("{{  myVar  }}", ctx));
    }

    [Fact]
    public void MultipleTokens_InOneString_AllInterpolated()
    {
        var ctx = Ctx();
        ctx.Vars["first"] = "John";
        ctx.Vars["last"] = "Doe";

        var result = TelSetVariableNodeHandler.Resolve("{{first}} {{last}} called from {{caller.ani}}", ctx);

        Assert.Equal("John Doe called from +15551234567", result);
    }

    [Fact]
    public void NowTimezone_ReturnsResolvedTenantTimeZoneId()
    {
        var ctx = Ctx("America/Los_Angeles");
        Assert.Equal("America/Los_Angeles", TelSetVariableNodeHandler.Resolve("{{now.timezone}}", ctx));
    }

    [Fact]
    public void NowTimezone_InvalidTenantTimezone_FallsBackToUtc_DoesNotThrow()
    {
        var ctx = Ctx("Not/ARealZone");
        Assert.Equal("UTC", TelSetVariableNodeHandler.Resolve("{{now.timezone}}", ctx));
    }

    [Fact]
    public void NowTime_MatchesHhMmFormat()
    {
        var ctx = Ctx();
        var result = TelSetVariableNodeHandler.Resolve("{{now.time}}", ctx);
        Assert.Matches(new Regex(@"^\d{2}:\d{2}$"), result);
    }

    [Fact]
    public void NowDate_MatchesIsoDateFormat()
    {
        var ctx = Ctx();
        var result = TelSetVariableNodeHandler.Resolve("{{now.date}}", ctx);
        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2}$"), result);
    }

    [Fact]
    public void NowDayName_IsAValidDayOfWeekName()
    {
        var ctx = Ctx();
        var result = TelSetVariableNodeHandler.Resolve("{{now.day_name}}", ctx);
        Assert.True(Enum.TryParse<DayOfWeek>(result, out _), $"'{result}' is not a valid DayOfWeek name");
    }

    [Fact]
    public void NowUnknownSuffix_ResolvesToEmptyString()
    {
        var ctx = Ctx();
        Assert.Equal("", TelSetVariableNodeHandler.Resolve("{{now.nonsense}}", ctx));
    }

    [Fact]
    public void NowNamespace_IsCaseInsensitive()
    {
        var ctx = Ctx("America/New_York");
        Assert.Equal("America/New_York", TelSetVariableNodeHandler.Resolve("{{NOW.TIMEZONE}}", ctx));
    }
}
