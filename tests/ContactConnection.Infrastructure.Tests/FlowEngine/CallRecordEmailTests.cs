using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Commerce;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.FlowEngine.NodeHandlers;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.FlowEngine;

/// <summary>The customer email on the call record: email node "save to call record" and
/// set_variable {{call_record.email}}.</summary>
public class CallRecordEmailTests
{
    private static FlowExecutionContext Ctx() => new()
    {
        SessionId = Guid.NewGuid(), FlowId = Guid.NewGuid(), FlowVersion = 1, CallRecordId = Guid.NewGuid(),
        InteractionId = Guid.NewGuid(), AgentId = Guid.NewGuid(), TenantId = Guid.NewGuid(), CurrentNodeId = "n_email",
    };

    private static JsonObject EmailNode(bool save) => new()
    {
        ["type"] = "email", ["label"] = "Customer Email", ["outputVariable"] = "customer_email",
        ["required"] = true, ["checkARecord"] = false, ["checkMX"] = false, ["checkDisposable"] = false,
        ["saveToCallRecord"] = save,
        ["transitions"] = new JsonObject { ["default"] = "n_next" },
    };

    private static EmailNodeHandler Handler(ICallAddressService contact, bool valid = true)
    {
        var validator = new Mock<IEmailValidationService>();
        validator.Setup(v => v.ValidateAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailValidationResult(valid, null, null, null, valid));
        return new EmailNodeHandler(new VariableResolver(), validator.Object, contact);
    }

    [Fact]
    public async Task EmailNode_SaveToCallRecord_SavesAndUpdatesCallerEmail()
    {
        var contact = new Mock<ICallAddressService>();
        var ctx = Ctx();

        var result = await Handler(contact.Object).ExecuteAsync(EmailNode(save: true), ctx, "jane@example.com", "");

        Assert.Equal("n_next", result.NextNodeId);
        contact.Verify(c => c.SetEmailAsync(ctx.CallRecordId, "jane@example.com", It.IsAny<CancellationToken>()), Times.Once);
        var resolver = new VariableResolver();
        Assert.Equal("jane@example.com", resolver.Resolve("{{caller.email}}", ctx.ToVariableContext()));
        Assert.Equal("jane@example.com", resolver.Resolve("{{call_record.email}}", ctx.ToVariableContext()));
    }

    [Fact]
    public async Task EmailNode_InvalidOrNotEnabled_NeverSaves()
    {
        var contact = new Mock<ICallAddressService>(MockBehavior.Strict);
        var invalid = await Handler(contact.Object, valid: false).ExecuteAsync(EmailNode(save: true), Ctx(), "not-an-email", "");
        Assert.Null(invalid.NextNodeId);
        await Handler(contact.Object).ExecuteAsync(EmailNode(save: false), Ctx(), "jane@example.com", "");
    }

    [Fact]
    public async Task SetVariable_CallRecordEmail_FromEmailNodeOutput()
    {
        var ctx = Ctx();
        ctx.FlowVars["customer_email"] = """{"value":"jane@example.com","isFormatValid":true,"isDeliverable":true}""";
        var contact = new Mock<ICallAddressService>();
        var handler = new SetVariableNodeHandler(new VariableResolver(), Mock.Of<ISharedCallVariableStore>(), contact.Object);

        await handler.ExecuteAsync(new JsonObject
        {
            ["type"] = "set_variable",
            ["assignments"] = new JsonArray(
                new JsonObject { ["variable"] = "{{call_record.email}}", ["value"] = "{{flow.customer_email}}" },
                new JsonObject { ["variable"] = "call_record.email", ["value"] = "{{flow.never_set}}" }),
            ["transitions"] = new JsonObject { ["default"] = "n_next" },
        }, ctx, null, "");

        contact.Verify(c => c.SetEmailAsync(ctx.CallRecordId, "jane@example.com", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("jane@example.com", ctx.Caller["email"]);
    }

    [Fact]
    public async Task Service_SetEmail_TrimsAndIgnoresBlank()
    {
        var record = CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var repo = new Mock<ICallRecordRepository>();
        repo.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        var service = new CallAddressService(repo.Object, Mock.Of<ICartService>());

        await service.SetEmailAsync(record.Id, "  jane@example.com ");
        await service.SetEmailAsync(record.Id, " ");
        Assert.Equal("jane@example.com", record.Email);
    }

    [Theory]
    [InlineData("""{"value":"a@b.com"}""", "a@b.com")]
    [InlineData(" a@b.com ", "a@b.com")]
    [InlineData("not an email", null)]
    [InlineData("", null)]
    public void EmailValue(string input, string? expected)
        => Assert.Equal(expected, CallAddressVars.EmailValue(input));
}
