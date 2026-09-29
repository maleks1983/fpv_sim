using System.Text;

namespace FpvTelemetry;

public sealed class MspV2Parser
{
    private readonly List<byte> _buffer = [];
    private const ushort SetTextCommand = 0x3007;

    public event Action<string>? TextReceived;
    public event Action? CrcError;

    public void Feed(ReadOnlySpan<byte> data)
    {
        foreach (byte value in data) _buffer.Add(value);
        ParseAvailable();
    }

    private void ParseAvailable()
    {
        while (true)
        {
            int start = FindStart();
            if (start < 0)
            {
                if (_buffer.Count > 2) _buffer.RemoveRange(0, _buffer.Count - 2);
                return;
            }
            if (start > 0) _buffer.RemoveRange(0, start);
            if (_buffer.Count < 9) return;

            int payloadLength = _buffer[6] | (_buffer[7] << 8);
            if (payloadLength > 1024)
            {
                _buffer.RemoveAt(0);
                continue;
            }

            int packetLength = 3 + 1 + 2 + 2 + payloadLength + 1;
            if (_buffer.Count < packetLength) return;

            byte calculated = Crc8(CollectionsMarshal.AsSpan(_buffer).Slice(3, 5 + payloadLength));
            byte received = _buffer[packetLength - 1];
            if (calculated != received)
            {
                CrcError?.Invoke();
                _buffer.RemoveAt(0);
                continue;
            }

            ushort command = (ushort)(_buffer[4] | (_buffer[5] << 8));
            if (command == SetTextCommand && payloadLength >= 2)
            {
                int declaredLength = _buffer[9];
                int textLength = Math.Min(declaredLength, payloadLength - 2);
                string text = Encoding.ASCII.GetString(CollectionsMarshal.AsSpan(_buffer).Slice(10, textLength)).TrimEnd(' ', '\0');
                TextReceived?.Invoke(text);
            }
            _buffer.RemoveRange(0, packetLength);
        }
    }

    private int FindStart()
    {
        for (int i = 0; i <= _buffer.Count - 3; i++)
            if (_buffer[i] == (byte)'$' && _buffer[i + 1] == (byte)'X' && (_buffer[i + 2] == (byte)'<' || _buffer[i + 2] == (byte)'>')) return i;
        return -1;
    }

    private static byte Crc8(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (byte)((crc << 1) ^ ((crc & 0x80) != 0 ? 0xD5 : 0));
        }
        return crc;
    }
}
