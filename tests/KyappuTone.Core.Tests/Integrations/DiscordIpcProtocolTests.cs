using System.Text;
using KyappuTone.Core.Integrations.Discord;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Integrations;

public class DiscordIpcProtocolTests
{
    [Fact]
    public void Encodes_opcode_and_payload_length_little_endian()
    {
        var frame = DiscordIpcProtocol.Encode(1, "{\"v\":1}");

        BitConverter.ToInt32(frame, 0).ShouldBe(1);
        BitConverter.ToInt32(frame, 4).ShouldBe(7);
        Encoding.UTF8.GetString(frame, 8, frame.Length - 8).ShouldBe("{\"v\":1}");
    }

    [Fact]
    public void Round_trips_a_handshake_payload()
    {
        var frame = DiscordIpcProtocol.Encode(0, "{\"v\":1,\"client_id\":\"123\"}");

        DiscordIpcProtocol.TryDecode(frame, out var opcode, out var json).ShouldBeTrue();
        opcode.ShouldBe(0);
        json.ShouldBe("{\"v\":1,\"client_id\":\"123\"}");
    }

    [Fact]
    public void Rejects_a_truncated_frame()
    {
        DiscordIpcProtocol.TryDecode(new byte[4], out _, out _).ShouldBeFalse();
        DiscordIpcProtocol.TryDecode(new byte[10], out _, out _).ShouldBeFalse();
    }
}
