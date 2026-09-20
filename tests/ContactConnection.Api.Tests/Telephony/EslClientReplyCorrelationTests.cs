using System.Net;
using System.Net.Sockets;
using System.Text;
using ContactConnection.Api.Telephony;
using Xunit;

namespace ContactConnection.Api.Tests.Telephony;

/// <summary>
/// Regression test for a live-caught bug (S149): EslClient used to do one blind read after
/// sending a command and treat whatever came back next as that command's reply. If FreeSWITCH
/// emitted a channel EVENT (e.g. CHANNEL_ANSWER, fired by uuid_answer) before that command's own
/// "api/response" reply arrived, the blind read consumed the event instead — leaving the real
/// reply to be wrongly picked up by the NEXT command's read. Caught live: two back-to-back
/// uuid_getvar calls came back with each other's answers, one of them literally the string
/// "+OK" (a stray reply from an unrelated prior command, not a variable value at all).
///
/// Spins up a minimal fake ESL server on a loopback TCP socket and scripts exactly that
/// interleaving, byte-for-byte, to verify EslClient's ReadReplyAsync/ReadMessageAsync split
/// correctly separates command replies (Content-Type api/response or command/reply) from
/// events (text/event-plain) regardless of arrival order.
/// </summary>
public class EslClientReplyCorrelationTests : IAsyncLifetime
{
    private TcpListener? _listener;
    private Task? _serverTask;

    public Task InitializeAsync()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _listener?.Stop();
        return Task.CompletedTask;
    }

    private static string Msg(string headers, string? body = null)
    {
        if (body is null) return headers + "\n\n";
        return $"{headers}\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\n\n{body}";
    }

    [Fact]
    public async Task CommandReply_NotConfusedWithInterleavedEvent_AndEventIsNotLost()
    {
        var port = ((IPEndPoint)_listener!.LocalEndpoint).Port;

        _serverTask = Task.Run(async () =>
        {
            using var socket = await _listener.AcceptTcpClientAsync();
            var stream = socket.GetStream();
            var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

            // Drain whatever the client writes — this test only cares about the order of bytes
            // the SERVER sends, not real request/response synchronization.
            _ = Task.Run(async () =>
            {
                var sink = new byte[4096];
                try { while (await stream.ReadAsync(sink, CancellationToken.None) > 0) { } }
                catch { /* connection closing — expected */ }
            });

            await writer.WriteAsync(Msg("Content-Type: auth/request"));
            await writer.WriteAsync(Msg("Content-Type: command/reply\nReply-Text: +OK accepted"));

            // The interleaving that broke the old blind-read: an event lands BEFORE the reply to
            // the command that triggered it (uuid_answer → CHANNEL_ANSWER), then the real reply.
            await writer.WriteAsync(Msg("Content-Type: text/event-plain\nEvent-Name: CHANNEL_ANSWER"));
            await writer.WriteAsync(Msg("Content-Type: api/response", "+OK"));

            // Second command's reply — must NOT be the queued event, and must carry ITS OWN
            // answer, not the first command's.
            await writer.WriteAsync(Msg("Content-Type: api/response", "opus"));

            await Task.Delay(2000);
        });

        var client = new EslClient();
        await client.ConnectAsync("127.0.0.1", port, "ClueCon", CancellationToken.None);

        // Old bug: this either hung (waiting on a reply consumed as an "event" with no
        // Content-Type check) or silently returned the wrong thing. Must complete and not throw.
        await client.AnswerChannelAsync("test-uuid", CancellationToken.None);

        var readCodec = await client.GetChannelVarAsync("test-uuid", "read_codec", CancellationToken.None);
        Assert.Equal("opus", readCodec); // NOT "+OK" — the bug's exact live symptom

        // The interleaved event must still be observable by the main event loop, not dropped.
        var queuedEvent = await client.ReadMessageAsync(CancellationToken.None);
        Assert.Equal("text/event-plain", queuedEvent?.ContentType);
        Assert.Equal("CHANNEL_ANSWER", queuedEvent?.GetHeader("Event-Name"));
    }
}
