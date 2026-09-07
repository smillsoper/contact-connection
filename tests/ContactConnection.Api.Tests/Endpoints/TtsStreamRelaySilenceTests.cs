using System;
using System.Text.Json;
using ContactConnection.Api.Endpoints;
using Xunit;

namespace ContactConnection.Api.Tests.Endpoints;

/// <summary>
/// Covers TtsStreamRelayEndpoints.BuildRawSilenceFrame — the lead-in silence chunk prepended to a
/// vendor TTS stream (S118) so RTP is already flowing before the first real syllable plays, the
/// streaming-path counterpart to S117's silence_stream priming on the file / flite paths.
/// </summary>
public class TtsStreamRelaySilenceTests
{
    [Theory]
    [InlineData(300, 8000, 4800)]   // 0.300s * 8000 * 2 bytes
    [InlineData(400, 8000, 6400)]
    [InlineData(120, 16000, 3840)]
    public void BuildRawSilenceFrame_emits_correct_length_of_zeros(int ms, int sampleRate, int expectedBytes)
    {
        var json = TtsStreamRelayEndpoints.BuildRawSilenceFrame(ms, sampleRate);

        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");

        Assert.Equal("streamAudio", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("raw", data.GetProperty("audioDataType").GetString());
        Assert.Equal(sampleRate, data.GetProperty("sampleRate").GetInt32());

        var pcm = Convert.FromBase64String(data.GetProperty("audioData").GetString()!);
        Assert.Equal(expectedBytes, pcm.Length);
        Assert.All(pcm, b => Assert.Equal(0, b));
    }
}
