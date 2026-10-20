using System.Buffers.Binary;
using System.Text;

namespace KyappuTone.Core.Integrations.Discord;

/// <summary>Frames and parses Discord's local IPC protocol.</summary>
public static class DiscordIpcProtocol
{
    public static byte[] Encode(int opcode, string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var frame = new byte[8 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), payload.Length);
        payload.CopyTo(frame, 8);
        return frame;
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, out int opcode, out string json)
    {
        opcode = 0;
        json = string.Empty;
        if (data.Length < 8)
            return false;

        var frameOpcode = BinaryPrimitives.ReadInt32LittleEndian(data[..4]);
        var length = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4, 4));
        if (length < 0 || data.Length != 8 + length)
            return false;

        opcode = frameOpcode;
        json = Encoding.UTF8.GetString(data.Slice(8, length));
        return true;
    }
}
