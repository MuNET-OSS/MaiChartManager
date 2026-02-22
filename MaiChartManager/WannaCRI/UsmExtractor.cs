using System.Buffers.Binary;
using System.Text;

namespace MaiChartManager.WannaCRI;

internal static class UsmExtractor
{
    private static ReadOnlySpan<byte> VideoChunkSignature => "@SFV"u8;
    private static ReadOnlySpan<byte> AudioChunkSignature => "@SFA"u8;
    private static ReadOnlySpan<byte> HcaSignature => "HCA\0"u8;
    private static ReadOnlySpan<byte> IvfSignature => "DKIF"u8;

    private const byte StreamPayloadType = 0;

    public static void Extract(string src, string output, ulong key)
    {
        var outputFolder = Path.Combine(output, SanitizeSegment(Path.GetFileName(src)));
        Directory.CreateDirectory(outputFolder);

        var videosPath = Path.Combine(outputFolder, "videos");
        var audiosPath = Path.Combine(outputFolder, "audios");
        Directory.CreateDirectory(videosPath);
        Directory.CreateDirectory(audiosPath);

        var (videoKey, audioKey) = GenerateKeys(key);

        var videoStreams = new Dictionary<byte, FileStream>();
        var audioStreams = new Dictionary<byte, FileStream>();

        try
        {
            using var source = File.OpenRead(src);
            var header = new byte[0x20];

            while (true)
            {
                var headerRead = source.Read(header, 0, header.Length);
                if (headerRead == 0)
                {
                    break;
                }

                if (headerRead < header.Length)
                {
                    throw new EndOfStreamException("Unexpected EOF while reading USM chunk header.");
                }

                var chunkSizeAfterHeader = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4, 4));
                var payloadOffset = header[9];
                var paddingSize = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(10, 2));
                var channelNumber = header[12];
                var payloadType = (byte)(header[15] & 0x03);

                var payloadSize = checked((int)chunkSizeAfterHeader - payloadOffset - paddingSize);
                if (payloadSize < 0)
                {
                    throw new InvalidDataException(
                        $"Invalid payload size in USM chunk (size={chunkSizeAfterHeader}, offset={payloadOffset}, padding={paddingSize}).");
                }

                var extraOffsetBytes = Math.Max(0, payloadOffset - 0x18);
                if (extraOffsetBytes > 0)
                {
                    SkipExactly(source, extraOffsetBytes);
                }

                var payload = new byte[payloadSize];
                ReadExactly(source, payload);

                if (paddingSize > 0)
                {
                    SkipExactly(source, paddingSize);
                }

                if (payloadType != StreamPayloadType || payload.Length == 0)
                {
                    continue;
                }

                var signature = header.AsSpan(0, 4);
                if (signature.SequenceEqual(VideoChunkSignature))
                {
                    var decoded = DecryptVideoPacket(payload, videoKey);
                    var outputStream = GetOrCreateVideoStream(videoStreams, channelNumber, videosPath, decoded);
                    outputStream.Write(decoded, 0, decoded.Length);
                }
                else if (signature.SequenceEqual(AudioChunkSignature))
                {
                    var decoded = DecryptAudioPacket(payload, audioKey);
                    var outputStream = GetOrCreateAudioStream(audioStreams, channelNumber, audiosPath, decoded);
                    outputStream.Write(decoded, 0, decoded.Length);
                }
            }
        }
        finally
        {
            foreach (var stream in videoStreams.Values)
            {
                stream.Dispose();
            }

            foreach (var stream in audioStreams.Values)
            {
                stream.Dispose();
            }
        }
    }

    private static FileStream GetOrCreateVideoStream(
        Dictionary<byte, FileStream> streams,
        byte channel,
        string videosPath,
        byte[] firstPayload)
    {
        if (streams.TryGetValue(channel, out var stream))
        {
            return stream;
        }

        var ext = DetectVideoExtension(firstPayload);
        var path = Path.Combine(videosPath, $"video_{channel:00}{ext}");
        stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        streams[channel] = stream;
        return stream;
    }

    private static FileStream GetOrCreateAudioStream(
        Dictionary<byte, FileStream> streams,
        byte channel,
        string audiosPath,
        byte[] firstPayload)
    {
        if (streams.TryGetValue(channel, out var stream))
        {
            return stream;
        }

        var ext = firstPayload.AsSpan().StartsWith(HcaSignature) ? ".hca" : ".bin";
        var path = Path.Combine(audiosPath, $"audio_{channel:00}{ext}");
        stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        streams[channel] = stream;
        return stream;
    }

    private static string DetectVideoExtension(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(IvfSignature))
        {
            return ".ivf";
        }

        if (data.Length >= 4 && data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x00 && data[3] == 0x01)
        {
            return ".h264";
        }

        if (data.Length >= 3 && data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x01)
        {
            return ".h264";
        }

        return ".bin";
    }

    private static byte[] DecryptVideoPacket(byte[] packet, byte[] videoKey)
    {
        if (videoKey.Length < 0x40)
        {
            throw new ArgumentException("Video key should be 0x40 bytes long.", nameof(videoKey));
        }

        var data = (byte[])packet.Clone();
        var encryptedPartSize = data.Length - 0x40;
        if (encryptedPartSize < 0x200)
        {
            return data;
        }

        var rolling = (byte[])videoKey.Clone();

        for (var i = 0x100; i < encryptedPartSize; i++)
        {
            var packetIndex = 0x40 + i;
            var keyIndex = 0x20 + i % 0x20;
            data[packetIndex] = (byte)(data[packetIndex] ^ rolling[keyIndex]);
            rolling[keyIndex] = (byte)(data[packetIndex] ^ videoKey[keyIndex]);
        }

        for (var i = 0; i < 0x100; i++)
        {
            var keyIndex = i % 0x20;
            rolling[keyIndex] = (byte)(rolling[keyIndex] ^ data[0x140 + i]);
            data[0x40 + i] = (byte)(data[0x40 + i] ^ rolling[keyIndex]);
        }

        return data;
    }

    private static byte[] DecryptAudioPacket(byte[] packet, byte[] audioKey)
    {
        var data = (byte[])packet.Clone();
        if (data.Length <= 0x140)
        {
            return data;
        }

        for (var i = 0x140; i < data.Length; i++)
        {
            data[i] = (byte)(data[i] ^ audioKey[i % 0x20]);
        }

        return data;
    }

    private static (byte[] videoKey, byte[] audioKey) GenerateKeys(ulong keyNum)
    {
        Span<byte> cipherKey = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(cipherKey, keyNum);

        Span<byte> key = stackalloc byte[0x20];
        key[0x00] = cipherKey[0];
        key[0x01] = cipherKey[1];
        key[0x02] = cipherKey[2];
        key[0x03] = unchecked((byte)(cipherKey[3] - 0x34));
        key[0x04] = unchecked((byte)(cipherKey[4] + 0xF9));
        key[0x05] = (byte)(cipherKey[5] ^ 0x13);
        key[0x06] = unchecked((byte)(cipherKey[6] + 0x61));
        key[0x07] = (byte)(key[0x00] ^ 0xFF);
        key[0x08] = unchecked((byte)(key[0x01] + key[0x02]));
        key[0x09] = unchecked((byte)(key[0x01] - key[0x07]));
        key[0x0A] = (byte)(key[0x02] ^ 0xFF);
        key[0x0B] = (byte)(key[0x01] ^ 0xFF);
        key[0x0C] = unchecked((byte)(key[0x0B] + key[0x09]));
        key[0x0D] = unchecked((byte)(key[0x08] - key[0x03]));
        key[0x0E] = (byte)(key[0x0D] ^ 0xFF);
        key[0x0F] = unchecked((byte)(key[0x0A] - key[0x0B]));
        key[0x10] = unchecked((byte)(key[0x08] - key[0x0F]));
        key[0x11] = (byte)(key[0x10] ^ key[0x07]);
        key[0x12] = (byte)(key[0x0F] ^ 0xFF);
        key[0x13] = (byte)(key[0x03] ^ 0x10);
        key[0x14] = unchecked((byte)(key[0x04] - 0x32));
        key[0x15] = unchecked((byte)(key[0x05] + 0xED));
        key[0x16] = (byte)(key[0x06] ^ 0xF3);
        key[0x17] = unchecked((byte)(key[0x13] - key[0x0F]));
        key[0x18] = unchecked((byte)(key[0x15] + key[0x07]));
        key[0x19] = unchecked((byte)(0x21 - key[0x13]));
        key[0x1A] = (byte)(key[0x14] ^ key[0x17]);
        key[0x1B] = unchecked((byte)(key[0x16] + key[0x16]));
        key[0x1C] = unchecked((byte)(key[0x17] + 0x44));
        key[0x1D] = unchecked((byte)(key[0x03] + key[0x04]));
        key[0x1E] = unchecked((byte)(key[0x05] - key[0x16]));
        key[0x1F] = (byte)(key[0x1D] ^ key[0x13]);

        Span<byte> audioTemplate = stackalloc byte[] { (byte)'U', (byte)'R', (byte)'U', (byte)'C' };
        var videoKey = new byte[0x40];
        var audioKey = new byte[0x20];

        for (var i = 0; i < 0x20; i++)
        {
            videoKey[i] = key[i];
            videoKey[0x20 + i] = (byte)(key[i] ^ 0xFF);
            audioKey[i] = i % 2 != 0 ? audioTemplate[(i >> 1) % 4] : (byte)(key[i] ^ 0xFF);
        }

        return (videoKey, audioKey);
    }

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0)
            {
                throw new EndOfStreamException("Unexpected EOF while reading USM payload.");
            }

            offset += read;
        }
    }

    private static void SkipExactly(Stream stream, int count)
    {
        if (count <= 0)
        {
            return;
        }

        if (stream.CanSeek)
        {
            stream.Seek(count, SeekOrigin.Current);
            return;
        }

        Span<byte> buffer = stackalloc byte[4096];
        var remaining = count;
        while (remaining > 0)
        {
            var current = Math.Min(remaining, buffer.Length);
            var read = stream.Read(buffer[..current]);
            if (read <= 0)
            {
                throw new EndOfStreamException("Unexpected EOF while skipping USM bytes.");
            }

            remaining -= read;
        }
    }

    private static string SanitizeSegment(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            builder.Append(Array.IndexOf(invalidChars, ch) >= 0 ? '_' : ch);
        }

        var result = builder.ToString().Trim();
        return string.IsNullOrEmpty(result) ? "output" : result;
    }
}

