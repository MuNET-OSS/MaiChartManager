using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MaiChartManager.WannaCRI;

internal static class UsmCreator
{
    private const int UsmFormatVersion = 16777984;
    private const int Vp9VideoCridFormatVersion = 16777984;
    private const int H264VideoCridFormatVersion = 0;

    private static ReadOnlySpan<byte> CridSignature => "CRID"u8;
    private static ReadOnlySpan<byte> VideoSignature => "@SFV"u8;
    private static ReadOnlySpan<byte> ContentsEndPayload => "#CONTENTS END   ===============\0"u8;
    private static ReadOnlySpan<byte> HeaderEndPayload => "#HEADER END     ===============\0"u8;
    private static ReadOnlySpan<byte> MetadataEndPayload => "#METADATA END   ===============\0"u8;

    private const byte PayloadTypeStream = 0;
    private const byte PayloadTypeHeader = 1;
    private const byte PayloadTypeSectionEnd = 2;
    private const byte PayloadTypeMetadata = 3;

    private static readonly JsonSerializerOptions ProbeJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Encoding UsmEncoding = CreateUsmEncoding();

    private static readonly ConcurrentDictionary<string, VideoProbeInfo> ProbeCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly byte[] PaddingZeros = new byte[0x20];

    public static void Create(string src, string? output, ulong key)
    {
        if (string.IsNullOrWhiteSpace(src))
        {
            throw new ArgumentException("Input path is required.", nameof(src));
        }

        if (!File.Exists(src))
        {
            throw new FileNotFoundException("Input video file not found.", src);
        }

        var outputPath = string.IsNullOrWhiteSpace(output)
            ? Path.ChangeExtension(src, ".usm")
            : output;

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Cannot infer output path.", nameof(output));
        }

        var preparedInput = PrepareVideoInput(src);
        try
        {
            var video = preparedInput.Video;
            var codecSettings = ResolveCodecSettings(video);
            var sourceFileSize = new FileInfo(preparedInput.StreamPath).Length;
            var frameSizes = BuildFrameSizes(video.PacketOffsets, sourceFileSize);
            if (frameSizes.Count == 0)
            {
                throw new InvalidDataException("No frames were found in input video.");
            }
            var keyframeIndices = new HashSet<int>();
            for (var i = 0; i < video.PacketIsKeyframe.Count; i++)
            {
                if (video.PacketIsKeyframe[i])
                {
                    keyframeIndices.Add(i);
                }
            }
            var maxSize = frameSizes.Max();
            var maxPaddingSize = GetAlignmentPadding(maxSize, 0x20);
            var maxPackedSize = 0x18 + maxSize + maxPaddingSize;
            var videoCridPage = CreateVideoCridPage(
                filename: Path.GetFileName(src),
                filesize: checked((int)sourceFileSize),
                maxSize: maxSize,
                formatVersion: codecSettings.VideoCridFormatVersion,
                channelNumber: 0,
                bitrate: video.Bitrate);
            var videoHeaderPage = CreateVideoHeaderPage(
                width: video.Width,
                height: video.Height,
                numFrames: frameSizes.Count,
                numKeyframes: keyframeIndices.Count,
                frameRate: video.FrameRate,
                maxPackedSize: maxPackedSize,
                mpegCodec: codecSettings.MpegCodec,
                mpegDcPrec: codecSettings.MpegDcPrec);
            var (videoKey, _) = GenerateKeys(key);
            var tempPath = Path.GetTempFileName();
            try
            {
                using var packedStreamFile = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                var streamResult = PackVideoStream(
                    preparedInput.StreamPath,
                    frameSizes,
                    keyframeIndices,
                    video.FrameRate,
                    videoKey,
                    packedStreamFile);
                var prestreamChunks = BuildPrestreamChunks(
                    videoCridPage,
                    videoHeaderPage,
                    streamResult.KeyframeOffsets,
                    streamResult.MaxPacketSize,
                    streamResult.StreamSize,
                    video.Bitrate);
                var fullOutputPath = Path.GetFullPath(outputPath);
                var outputDirectory = Path.GetDirectoryName(fullOutputPath);
                if (!string.IsNullOrEmpty(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }
                using var outputFile = new FileStream(fullOutputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                foreach (var chunk in prestreamChunks)
                {
                    outputFile.Write(chunk, 0, chunk.Length);
                }
                packedStreamFile.Position = 0;
                packedStreamFile.CopyTo(outputFile);
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }
        finally
        {
            if (preparedInput.DeleteAfterUse)
            {
                TryDeleteFile(preparedInput.StreamPath);
            }
        }
    }

    private static List<byte[]> BuildPrestreamChunks(
        UsmPage videoCridPage,
        UsmPage videoHeaderPage,
        List<(int FrameIndex, long Offset)> keyframeOffsets,
        int maxPacketSize,
        long streamFileSize,
        int totalBitrate)
    {
        var chunks = new List<byte[]>();

        var videoHeaderPayload = PackPages([videoHeaderPage], UsmEncoding);
        var videoHeaderChunk = PackChunk(
            VideoSignature,
            PayloadTypeHeader,
            videoHeaderPayload,
            frameRate: 30,
            frameTime: 0,
            padding: 0x18,
            channelNumber: 0);
        chunks.Add(videoHeaderChunk);

        var headerEndChunk = PackChunk(
            VideoSignature,
            PayloadTypeSectionEnd,
            HeaderEndPayload,
            frameRate: 30,
            frameTime: 0,
            padding: 0,
            channelNumber: 0);
        chunks.Add(headerEndChunk);

        var currentPosition = videoHeaderChunk.Length + headerEndChunk.Length;

        var metadataPages = new List<UsmPage>(keyframeOffsets.Count);
        foreach (var (frameIndex, offset) in keyframeOffsets)
        {
            var page = new UsmPage("VIDEO_SEEKINFO");
            page.Update("ofs_byte", ElementType.LONGLONG, offset);
            page.Update("ofs_frmid", ElementType.UINT, frameIndex);
            page.Update("num_skip", ElementType.USHORT, 0);
            page.Update("resv", ElementType.USHORT, 0);
            metadataPages.Add(page);
        }

        var metadataPayload = PackPages(metadataPages, UsmEncoding);
        var metadataPadding = MetadataPadding(0x20 + metadataPayload.Length);
        var metadataChunk = PackChunk(
            VideoSignature,
            PayloadTypeMetadata,
            metadataPayload,
            frameRate: 30,
            frameTime: 0,
            padding: metadataPadding,
            channelNumber: 0);

        var metadataEndChunk = PackChunk(
            VideoSignature,
            PayloadTypeSectionEnd,
            MetadataEndPayload,
            frameRate: 30,
            frameTime: 0,
            padding: 0,
            channelNumber: 0);

        var metadataSectionSize = metadataChunk.Length + metadataEndChunk.Length;

        if (metadataPages.Count > 0)
        {
            foreach (var page in metadataPages)
            {
                var original = Convert.ToInt64(page.Get("ofs_byte").Value, CultureInfo.InvariantCulture);
                var adjusted = original + 0x800L + currentPosition + metadataSectionSize;
                page.Update("ofs_byte", ElementType.LONGLONG, adjusted);
            }

            metadataPayload = PackPages(metadataPages, UsmEncoding);
            metadataChunk = PackChunk(
                VideoSignature,
                PayloadTypeMetadata,
                metadataPayload,
                frameRate: 30,
                frameTime: 0,
                padding: MetadataPadding(0x20 + metadataPayload.Length),
                channelNumber: 0);
            metadataSectionSize = metadataChunk.Length + metadataEndChunk.Length;
        }

        var headerMetadataSize = currentPosition + metadataSectionSize;
        var sizeAfterCridPart = checked((int)(0x800L + headerMetadataSize + streamFileSize));
        var usmCridPage = CreateUsmCridPage(
            filename: Path.ChangeExtension(videoCridPage.Get("filename").Value.ToString() ?? "movie", ".usm"),
            sizeAfterCridPart: sizeAfterCridPart,
            maxPacketSize: maxPacketSize,
            bitrate: totalBitrate,
            formatVersion: UsmFormatVersion);

        var infoPayload = PackPages([usmCridPage, videoCridPage], UsmEncoding);
        var infoPadding = PadToNextSector(position: 0, chunkSize: 0x20 + infoPayload.Length);
        var infoChunk = PackChunk(
            CridSignature,
            PayloadTypeHeader,
            infoPayload,
            frameRate: 30,
            frameTime: 0,
            padding: infoPadding,
            channelNumber: 0);

        return [infoChunk, videoHeaderChunk, headerEndChunk, metadataChunk, metadataEndChunk];
    }

    private static StreamPackResult PackVideoStream(
        string sourcePath,
        IReadOnlyList<int> frameSizes,
        HashSet<int> keyframeIndices,
        double frameRate,
        byte[] videoKey,
        Stream output)
    {
        using var source = File.OpenRead(sourcePath);

        var keyframeOffsets = new List<(int FrameIndex, long Offset)>();
        var maxPacketSize = 1;
        var frameRateValue = checked((int)(frameRate * 100));
        var maxFrameSize = frameSizes.Count == 0 ? 0 : frameSizes.Max();
        var rentedBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, maxFrameSize));

        try
        {
            for (var i = 0; i < frameSizes.Count; i++)
            {
                var frameSize = frameSizes[i];
                var payload = rentedBuffer.AsSpan(0, frameSize);
                ReadExactly(source, payload);
                EncryptVideoPacketInPlace(payload, videoKey);

                if (keyframeIndices.Contains(i))
                {
                    keyframeOffsets.Add((i, output.Position));
                }

                var framePadding = GetAlignmentPadding(frameSize, 0x20);
                var frameTime = checked((int)(i * 99.9));
                var frameChunkSize = WriteChunkToStream(
                    output,
                    VideoSignature,
                    PayloadTypeStream,
                    payload,
                    frameRate: frameRateValue,
                    frameTime: frameTime,
                    padding: framePadding,
                    channelNumber: 0);

                maxPacketSize = Math.Max(maxPacketSize, frameChunkSize);

                if (i == frameSizes.Count - 1)
                {
                    var endChunkSize = WriteChunkToStream(
                        output,
                        VideoSignature,
                        PayloadTypeSectionEnd,
                        ContentsEndPayload,
                        frameRate: frameRateValue,
                        frameTime: 0,
                        padding: 0,
                        channelNumber: 0);

                    maxPacketSize = Math.Max(maxPacketSize, endChunkSize);
                }
            }

            output.Flush();
            return new StreamPackResult(output.Length, maxPacketSize, keyframeOffsets);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }

    private static PreparedVideoInput PrepareVideoInput(string sourcePath)
    {
        var sourceProbe = ProbeVideo(sourcePath, includePackets: IsLikelyElementaryStream(sourcePath));
        if (!string.Equals(sourceProbe.CodecName, "h264", StringComparison.OrdinalIgnoreCase))
        {
            var fullProbe = sourceProbe.PacketOffsets.Count > 0 ? sourceProbe : ProbeVideo(sourcePath, includePackets: true);
            return new PreparedVideoInput(sourcePath, fullProbe, DeleteAfterUse: false);
        }

        if (FormatContains(sourceProbe.FormatName, "h264"))
        {
            var fullProbe = sourceProbe.PacketOffsets.Count > 0 ? sourceProbe : ProbeVideo(sourcePath, includePackets: true);
            return new PreparedVideoInput(sourcePath, fullProbe, DeleteAfterUse: false);
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"maichartmanager_createusm_{Guid.NewGuid():N}.h264");
        try
        {
            ConvertH264ToAnnexB(sourcePath, tempPath);
            var convertedProbe = ProbeVideo(tempPath, includePackets: true);
            return new PreparedVideoInput(tempPath, convertedProbe, DeleteAfterUse: true);
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    private static bool IsLikelyElementaryStream(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath);
        return extension.Equals(".h264", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".264", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".ivf", StringComparison.OrdinalIgnoreCase);
    }

    private static void ConvertH264ToAnnexB(string sourcePath, string outputPath)
    {
        var ffmpeg = ResolveFfmpegPath();
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("0:v:0");
        startInfo.ArgumentList.Add("-c:v");
        startInfo.ArgumentList.Add("copy");
        startInfo.ArgumentList.Add("-an");
        startInfo.ArgumentList.Add("-sn");
        startInfo.ArgumentList.Add("-dn");
        startInfo.ArgumentList.Add("-bsf:v");
        startInfo.ArgumentList.Add("h264_mp4toannexb");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("h264");
        startInfo.ArgumentList.Add(outputPath);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ffmpeg.");
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var stderr = stderrTask.GetAwaiter().GetResult().Trim();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg failed while converting H264 stream: {stderr}");
        }
    }
    private static VideoCodecSettings ResolveCodecSettings(VideoProbeInfo video)
    {
        if (string.Equals(video.CodecName, "vp9", StringComparison.OrdinalIgnoreCase))
        {
            if (!FormatContains(video.FormatName, "ivf"))
            {
                throw new NotSupportedException($"VP9 input must be IVF. Detected format: {video.FormatName}.");
            }

            return new VideoCodecSettings(
                VideoCridFormatVersion: Vp9VideoCridFormatVersion,
                MpegCodec: 9,
                MpegDcPrec: 0);
        }

        if (string.Equals(video.CodecName, "h264", StringComparison.OrdinalIgnoreCase))
        {
            if (!FormatContains(video.FormatName, "h264"))
            {
                throw new NotSupportedException($"H264 input must be raw H264 stream (format_name=h264). Detected format: {video.FormatName}.");
            }

            return new VideoCodecSettings(
                VideoCridFormatVersion: H264VideoCridFormatVersion,
                MpegCodec: 5,
                MpegDcPrec: 11);
        }

        throw new NotSupportedException($"Unsupported codec for createusm: {video.CodecName}. Supported codecs: vp9 (ivf), h264 (raw stream).");
    }

    private static bool FormatContains(string formatName, string expectedToken)
    {
        if (string.IsNullOrWhiteSpace(formatName))
        {
            return false;
        }

        return formatName
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(it => string.Equals(it, expectedToken, StringComparison.OrdinalIgnoreCase));
    }
    private static UsmPage CreateVideoCridPage(
        string filename,
        int filesize,
        int maxSize,
        int formatVersion,
        int channelNumber,
        int bitrate)
    {
        var page = new UsmPage("CRIUSF_DIR_STREAM");
        page.Update("fmtver", ElementType.INT, formatVersion);
        page.Update("filename", ElementType.STRING, filename);
        page.Update("filesize", ElementType.INT, filesize);
        page.Update("datasize", ElementType.INT, 0);
        page.Update("stmid", ElementType.INT, 1079199318);
        page.Update("chno", ElementType.SHORT, channelNumber);
        page.Update("minchk", ElementType.SHORT, 3);
        page.Update("minbuf", ElementType.INT, maxSize);
        page.Update("avbps", ElementType.INT, bitrate);
        return page;
    }

    private static UsmPage CreateVideoHeaderPage(
        int width,
        int height,
        int numFrames,
        int numKeyframes,
        double frameRate,
        int maxPackedSize,
        int mpegCodec,
        int mpegDcPrec)
    {
        var page = new UsmPage("VIDEO_HDRINFO");
        page.Update("width", ElementType.INT, width);
        page.Update("height", ElementType.INT, height);
        page.Update("mat_width", ElementType.INT, width);
        page.Update("mat_height", ElementType.INT, height);
        page.Update("disp_width", ElementType.INT, width);
        page.Update("disp_height", ElementType.INT, height);
        page.Update("scrn_width", ElementType.INT, 0);
        page.Update("mpeg_dcprec", ElementType.CHAR, mpegDcPrec);
        page.Update("mpeg_codec", ElementType.CHAR, mpegCodec);
        page.Update("alpha_type", ElementType.INT, 0);
        page.Update("total_frames", ElementType.INT, numFrames);
        page.Update("framerate_n", ElementType.INT, checked((int)(frameRate * 1000)));
        page.Update("framerate_d", ElementType.INT, 1000);
        page.Update("metadata_count", ElementType.INT, 1);
        page.Update("metadata_size", ElementType.INT, numKeyframes);
        page.Update("ixsize", ElementType.INT, maxPackedSize);
        page.Update("pre_padding", ElementType.INT, 0);
        page.Update("max_picture_size", ElementType.INT, 0);
        page.Update("color_space", ElementType.INT, 0);
        page.Update("picture_type", ElementType.INT, 0);
        return page;
    }

    private static UsmPage CreateUsmCridPage(
        string filename,
        int sizeAfterCridPart,
        int maxPacketSize,
        int bitrate,
        int formatVersion)
    {
        var page = new UsmPage("CRIUSF_DIR_STREAM");
        page.Update("fmtver", ElementType.INT, formatVersion);
        page.Update("filename", ElementType.STRING, filename);
        page.Update("filesize", ElementType.INT, checked(0x800 + sizeAfterCridPart));
        page.Update("datasize", ElementType.INT, 0);
        page.Update("stmid", ElementType.INT, 0);
        page.Update("chno", ElementType.SHORT, -1);
        page.Update("minchk", ElementType.SHORT, 1);

        var minbuf = (int)Math.Round(maxPacketSize * 1.98746, MidpointRounding.ToEven);
        if (minbuf % 0x10 != 0)
        {
            minbuf += 0x10 - (minbuf % 0x10);
        }

        page.Update("minbuf", ElementType.INT, minbuf);
        page.Update("avbps", ElementType.INT, bitrate);
        return page;
    }

    private static VideoProbeInfo ProbeVideo(string src, bool includePackets)
    {
        var fullPath = Path.GetFullPath(src);
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Input video file not found.", fullPath);
        }

        var cacheKey = $"{fullPath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}|{(includePackets ? 1 : 0)}";
        if (ProbeCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var ffprobe = ResolveFfprobePath();

        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        if (includePackets)
        {
            startInfo.ArgumentList.Add("-show_entries");
            startInfo.ArgumentList.Add("packet=pos,flags");
        }

        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add("stream=codec_name,width,height,r_frame_rate,bit_rate");
        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add("format=format_name,bit_rate");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add(fullPath);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ffprobe.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        process.WaitForExit();

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffprobe failed with exit code {process.ExitCode}: {stderr.Trim()}");
        }

        var result = JsonSerializer.Deserialize<FfprobeResult>(stdout, ProbeJsonOptions)
            ?? throw new InvalidDataException("ffprobe output is empty.");

        var stream = result.Streams?.FirstOrDefault()
            ?? throw new InvalidDataException("ffprobe output has no video stream.");

        var codecName = stream.CodecName ?? "";
        var formatName = result.Format?.FormatName ?? "";
        var width = stream.Width;
        var height = stream.Height;

        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("Invalid video dimensions returned by ffprobe.");
        }

        var frameRate = ParseFraction(stream.RFrameRate, fallback: 30.0);

        var bitrate = ParseInt(stream.BitRate);
        if (bitrate <= 0)
        {
            bitrate = ParseInt(result.Format?.BitRate);
        }

        var packetOffsets = new List<long>();
        var packetIsKeyframe = new List<bool>();
        if (includePackets && result.Packets is not null)
        {
            foreach (var packet in result.Packets)
            {
                if (packet?.Pos is null)
                {
                    continue;
                }

                if (!long.TryParse(packet.Pos, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset))
                {
                    continue;
                }

                packetOffsets.Add(offset);
                packetIsKeyframe.Add(packet.Flags?.Contains('K') == true);
            }
        }

        if (includePackets && packetOffsets.Count == 0)
        {
            throw new InvalidDataException("ffprobe returned no packet offsets.");
        }

        var probeInfo = new VideoProbeInfo(
            CodecName: codecName,
            FormatName: formatName,
            Width: width,
            Height: height,
            FrameRate: frameRate,
            Bitrate: bitrate,
            PacketOffsets: packetOffsets,
            PacketIsKeyframe: packetIsKeyframe);

        ProbeCache[cacheKey] = probeInfo;
        return probeInfo;
    }

    private static string ResolveFfprobePath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ffprobe.exe"),
            Path.Combine(AppContext.BaseDirectory, "FFMpeg", "ffprobe.exe"),
            Path.Combine(StaticSettings.exeDir, "ffprobe.exe"),
            Path.Combine(StaticSettings.exeDir, "FFMpeg", "ffprobe.exe")
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return "ffprobe";
    }
    private static string ResolveFfmpegPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "FFMpeg", "ffmpeg.exe"),
            Path.Combine(StaticSettings.exeDir, "ffmpeg.exe"),
            Path.Combine(StaticSettings.exeDir, "FFMpeg", "ffmpeg.exe")
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return "ffmpeg";
    }
    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore temporary cleanup failures.
        }
    }
    private static double ParseFraction(string? value, double fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var parts = value.Split('/');
        if (parts.Length != 2)
        {
            return fallback;
        }

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) ||
            Math.Abs(den) < double.Epsilon)
        {
            return fallback;
        }

        return num / den;
    }

    private static int ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;
    }

    private static List<int> BuildFrameSizes(IReadOnlyList<long> offsets, long fileSize)
    {
        var sizes = new List<int>(offsets.Count);

        for (var i = 0; i < offsets.Count; i++)
        {
            long frameSize;
            var frameOffset = offsets[i];

            if (i == offsets.Count - 1)
            {
                frameSize = fileSize - frameOffset;
            }
            else if (i == 0)
            {
                frameSize = offsets[i + 1];
            }
            else
            {
                frameSize = offsets[i + 1] - frameOffset;
            }

            if (frameSize <= 0 || frameSize > int.MaxValue)
            {
                throw new InvalidDataException($"Invalid frame size computed at index {i}: {frameSize}.");
            }

            sizes.Add((int)frameSize);
        }

        return sizes;
    }
    private static byte[] PackPages(IReadOnlyList<UsmPage> pages, Encoding encoding)
    {
        if (pages.Count == 0)
        {
            return [];
        }

        var pageName = pages[0].Name;
        var keys = pages[0].Elements.Select(element => element.Name).ToArray();
        foreach (var page in pages)
        {
            if (!string.Equals(pageName, page.Name, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Pages do not have the same name.");
            }

            if (page.Elements.Count != keys.Length)
            {
                throw new InvalidDataException("Pages do not have the same key count.");
            }

            foreach (var key in keys)
            {
                if (!page.Contains(key))
                {
                    throw new InvalidDataException("Pages do not have identical keys.");
                }
            }
        }

        var stringArray = new List<byte>();
        stringArray.AddRange(Encoding.UTF8.GetBytes("<NULL>"));
        stringArray.Add(0);

        var pageNameOffset = stringArray.Count;
        stringArray.AddRange(Encoding.UTF8.GetBytes(pageName));
        stringArray.Add(0);

        var elementNameOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var nameOffset = stringArray.Count;
            stringArray.AddRange(Encoding.UTF8.GetBytes(key));
            stringArray.Add(0);
            elementNameOffsets[key] = nameOffset;
        }

        var commonElements = new HashSet<string>(StringComparer.Ordinal);
        if (pages.Count > 1)
        {
            foreach (var key in keys)
            {
                var firstElement = pages[0].Get(key);
                var isCommon = true;
                for (var i = 1; i < pages.Count; i++)
                {
                    if (!ElementEquals(firstElement, pages[i].Get(key)))
                    {
                        isCommon = false;
                        break;
                    }
                }

                if (isCommon)
                {
                    commonElements.Add(key);
                }
            }
        }

        var sharedArray = new List<byte>();
        var uniqueArray = new List<byte>();
        var byteArray = new List<byte>();

        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var page = pages[pageIndex];
            foreach (var key in keys)
            {
                var element = page.Get(key);
                var elementTypePacked = (byte)element.Type;
                if (commonElements.Contains(key))
                {
                    if (pageIndex != 0)
                    {
                        continue;
                    }

                    elementTypePacked += unchecked((byte)(1 << 5));
                    sharedArray.Add(elementTypePacked);
                    AddUInt32BE(sharedArray, (uint)elementNameOffsets[key]);
                    PackElementValue(sharedArray, element, stringArray, byteArray, encoding);
                }
                else
                {
                    if (pageIndex == 0)
                    {
                        elementTypePacked += unchecked((byte)(2 << 5));
                        sharedArray.Add(elementTypePacked);
                        AddUInt32BE(sharedArray, (uint)elementNameOffsets[key]);
                    }

                    PackElementValue(uniqueArray, element, stringArray, byteArray, encoding);
                }
            }
        }

        var dataSize = 24 + sharedArray.Count + uniqueArray.Count + stringArray.Count + byteArray.Count;
        var uniqueArrayOffset = 24 + sharedArray.Count;
        var stringOffset = 24 + sharedArray.Count + uniqueArray.Count;
        var byteArrayOffset = 24 + sharedArray.Count + uniqueArray.Count + stringArray.Count;
        var uniqueArraySizePerPage = pages.Count == 0 ? 0 : uniqueArray.Count / pages.Count;

        var result = new List<byte>();
        result.AddRange("@UTF"u8.ToArray());
        AddUInt32BE(result, checked((uint)dataSize));
        AddUInt32BE(result, checked((uint)uniqueArrayOffset));
        AddUInt32BE(result, checked((uint)stringOffset));
        AddUInt32BE(result, checked((uint)byteArrayOffset));
        AddUInt32BE(result, checked((uint)pageNameOffset));
        AddUInt16BE(result, checked((ushort)keys.Length));
        AddUInt16BE(result, checked((ushort)uniqueArraySizePerPage));
        AddUInt32BE(result, checked((uint)pages.Count));
        result.AddRange(sharedArray);
        result.AddRange(uniqueArray);
        result.AddRange(stringArray);
        result.AddRange(byteArray);
        return result.ToArray();
    }

    private static void PackElementValue(
        List<byte> target,
        UsmPageElement element,
        List<byte> stringArray,
        List<byte> byteArray,
        Encoding encoding)
    {
        switch (element.Type)
        {
            case ElementType.CHAR:
            {
                var value = Convert.ToSByte(element.Value, CultureInfo.InvariantCulture);
                target.Add(unchecked((byte)value));
                break;
            }
            case ElementType.UCHAR:
            {
                target.Add(Convert.ToByte(element.Value, CultureInfo.InvariantCulture));
                break;
            }
            case ElementType.SHORT:
            {
                AddInt16BE(target, Convert.ToInt16(element.Value, CultureInfo.InvariantCulture));
                break;
            }
            case ElementType.USHORT:
            {
                AddUInt16BE(target, Convert.ToUInt16(element.Value, CultureInfo.InvariantCulture));
                break;
            }
            case ElementType.INT:
            {
                AddInt32BE(target, Convert.ToInt32(element.Value, CultureInfo.InvariantCulture));
                break;
            }
            case ElementType.UINT:
            {
                AddUInt32BE(target, Convert.ToUInt32(element.Value, CultureInfo.InvariantCulture));
                break;
            }
            case ElementType.LONGLONG:
            {
                AddInt64BE(target, Convert.ToInt64(element.Value, CultureInfo.InvariantCulture));
                break;
            }
            case ElementType.ULONGLONG:
            {
                AddUInt64BE(target, Convert.ToUInt64(element.Value, CultureInfo.InvariantCulture));
                break;
            }
            case ElementType.FLOAT:
            {
                var bytes = BitConverter.GetBytes(Convert.ToSingle(element.Value, CultureInfo.InvariantCulture));
                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(bytes);
                }

                target.AddRange(bytes);
                break;
            }
            case ElementType.STRING:
            {
                var valueOffset = stringArray.Count;
                var value = Convert.ToString(element.Value, CultureInfo.InvariantCulture) ?? string.Empty;
                stringArray.AddRange(encoding.GetBytes(value));
                stringArray.Add(0);
                AddUInt32BE(target, checked((uint)valueOffset));
                break;
            }
            case ElementType.BYTES:
            {
                if (element.Value is not byte[] bytes)
                {
                    throw new InvalidDataException("BYTES element value must be byte[].");
                }

                var bytesOffset = byteArray.Count;
                var bytesEnd = bytesOffset + bytes.Length;
                AddUInt32BE(target, checked((uint)bytesOffset));
                AddUInt32BE(target, checked((uint)bytesEnd));
                byteArray.AddRange(bytes);
                break;
            }
            default:
                throw new InvalidDataException($"Unknown element type: {element.Type}");
        }
    }

    private static bool ElementEquals(UsmPageElement left, UsmPageElement right)
    {
        if (left.Type != right.Type)
        {
            return false;
        }

        if (left.Value is byte[] leftBytes && right.Value is byte[] rightBytes)
        {
            return leftBytes.SequenceEqual(rightBytes);
        }

        return Equals(left.Value, right.Value);
    }

    private static byte[] PackChunk(
        ReadOnlySpan<byte> chunkType,
        byte payloadType,
        ReadOnlySpan<byte> payload,
        int frameRate,
        int frameTime,
        int padding,
        byte channelNumber)
    {
        var chunksize = checked(0x18 + payload.Length + padding);

        var result = new byte[0x20 + payload.Length + padding];
        var offset = 0;

        chunkType.CopyTo(result.AsSpan(offset, 4));
        offset += 4;

        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(offset, 4), checked((uint)chunksize));
        offset += 4;

        result[offset++] = 0;
        result[offset++] = 0x18;

        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset, 2), checked((ushort)padding));
        offset += 2;

        result[offset++] = channelNumber;
        result[offset++] = 0;
        result[offset++] = 0;
        result[offset++] = payloadType;

        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(offset, 4), frameTime);
        offset += 4;

        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(offset, 4), frameRate);
        offset += 4;

        offset += 8;

        payload.CopyTo(result.AsSpan(offset, payload.Length));
        return result;
    }

    private static int WriteChunkToStream(
        Stream output,
        ReadOnlySpan<byte> chunkType,
        byte payloadType,
        ReadOnlySpan<byte> payload,
        int frameRate,
        int frameTime,
        int padding,
        byte channelNumber)
    {
        Span<byte> header = stackalloc byte[0x20];
        var chunkSize = checked(0x18 + payload.Length + padding);

        var offset = 0;
        chunkType.CopyTo(header.Slice(offset, 4));
        offset += 4;

        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(offset, 4), checked((uint)chunkSize));
        offset += 4;

        header[offset++] = 0;
        header[offset++] = 0x18;
        BinaryPrimitives.WriteUInt16BigEndian(header.Slice(offset, 2), checked((ushort)padding));
        offset += 2;

        header[offset++] = channelNumber;
        header[offset++] = 0;
        header[offset++] = 0;
        header[offset++] = payloadType;

        BinaryPrimitives.WriteInt32BigEndian(header.Slice(offset, 4), frameTime);
        offset += 4;
        BinaryPrimitives.WriteInt32BigEndian(header.Slice(offset, 4), frameRate);

        output.Write(header);
        output.Write(payload);
        if (padding > 0)
        {
            output.Write(PaddingZeros.AsSpan(0, padding));
        }

        return 0x20 + payload.Length + padding;
    }

    private static int MetadataPadding(int chunkSize)
    {
        if (chunkSize <= 0xF0)
        {
            return 0xF0 - chunkSize;
        }

        var aligned = (chunkSize + 0x7) & ~0x7;
        return aligned - chunkSize;
    }

    private static int PadToNextSector(int position, int chunkSize)
    {
        var unpadded = position + chunkSize;
        var multiple = (unpadded + 0x7FF) / 0x800;
        return multiple * 0x800 - unpadded;
    }

    private static int GetAlignmentPadding(int size, int alignment)
    {
        var remainder = size % alignment;
        return remainder == 0 ? 0 : alignment - remainder;
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer[offset..]);
            if (read <= 0)
            {
                throw new EndOfStreamException("Unexpected EOF while reading video packet.");
            }

            offset += read;
        }
    }

    private static void EncryptVideoPacketInPlace(Span<byte> data, ReadOnlySpan<byte> videoKey)
    {
        if (videoKey.Length < 0x40)
        {
            throw new ArgumentException("Video key should be 0x40 bytes long.", nameof(videoKey));
        }

        if (data.Length < 0x240)
        {
            return;
        }

        Span<byte> rolling = stackalloc byte[0x40];
        videoKey.Slice(0, 0x40).CopyTo(rolling);

        var encryptedPartSize = data.Length - 0x40;
        for (var i = 0; i < 0x100; i++)
        {
            var keyIndex = i % 0x20;
            rolling[keyIndex] = (byte)(rolling[keyIndex] ^ data[0x140 + i]);
            data[0x40 + i] = (byte)(data[0x40 + i] ^ rolling[keyIndex]);
        }

        for (var i = 0x100; i < encryptedPartSize; i++)
        {
            var packetIndex = 0x40 + i;
            var keyIndex = 0x20 + i % 0x20;
            var plain = data[packetIndex];
            data[packetIndex] = (byte)(data[packetIndex] ^ rolling[keyIndex]);
            rolling[keyIndex] = (byte)(plain ^ videoKey[keyIndex]);
        }
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

    private static Encoding CreateUsmEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("shift-jis");
    }

    private static void AddInt16BE(List<byte> target, short value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        target.AddRange(buffer.ToArray());
    }

    private static void AddUInt16BE(List<byte> target, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        target.AddRange(buffer.ToArray());
    }

    private static void AddInt32BE(List<byte> target, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        target.AddRange(buffer.ToArray());
    }

    private static void AddUInt32BE(List<byte> target, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        target.AddRange(buffer.ToArray());
    }

    private static void AddInt64BE(List<byte> target, long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        target.AddRange(buffer.ToArray());
    }

    private static void AddUInt64BE(List<byte> target, ulong value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
        target.AddRange(buffer.ToArray());
    }

    private sealed record StreamPackResult(long StreamSize, int MaxPacketSize, List<(int FrameIndex, long Offset)> KeyframeOffsets);
    private sealed record PreparedVideoInput(string StreamPath, VideoProbeInfo Video, bool DeleteAfterUse);
    private sealed record VideoProbeInfo(
        string CodecName,
        string FormatName,
        int Width,
        int Height,
        double FrameRate,
        int Bitrate,
        List<long> PacketOffsets,
        List<bool> PacketIsKeyframe);

    private readonly record struct VideoCodecSettings(int VideoCridFormatVersion, int MpegCodec, int MpegDcPrec);

    private sealed class UsmPage
    {
        private readonly List<UsmPageElement> _elements = new();
        private readonly Dictionary<string, int> _indexes = new(StringComparer.Ordinal);

        public UsmPage(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public IReadOnlyList<UsmPageElement> Elements => _elements;

        public void Update(string name, ElementType type, object value)
        {
            if (string.Equals(name, "filename", StringComparison.Ordinal) && value is string stringValue)
            {
                value = stringValue.Replace("\\", "/", StringComparison.Ordinal);
            }

            if (_indexes.TryGetValue(name, out var index))
            {
                _elements[index] = new UsmPageElement(name, type, value);
            }
            else
            {
                _indexes[name] = _elements.Count;
                _elements.Add(new UsmPageElement(name, type, value));
            }
        }

        public bool Contains(string name)
        {
            return _indexes.ContainsKey(name);
        }

        public UsmPageElement Get(string name)
        {
            if (!_indexes.TryGetValue(name, out var index))
            {
                throw new KeyNotFoundException($"Key '{name}' does not exist in USM page '{Name}'.");
            }

            return _elements[index];
        }
    }

    private readonly record struct UsmPageElement(string Name, ElementType Type, object Value);

    private enum ElementType : byte
    {
        CHAR = 0x10,
        UCHAR = 0x11,
        SHORT = 0x12,
        USHORT = 0x13,
        INT = 0x14,
        UINT = 0x15,
        LONGLONG = 0x16,
        ULONGLONG = 0x17,
        FLOAT = 0x18,
        STRING = 0x1A,
        BYTES = 0x1B
    }

    private sealed class FfprobeResult
    {
        [JsonPropertyName("packets")]
        public List<FfprobePacket?>? Packets { get; set; }

        [JsonPropertyName("streams")]
        public List<FfprobeStream>? Streams { get; set; }

        [JsonPropertyName("format")]
        public FfprobeFormat? Format { get; set; }
    }

    private sealed class FfprobePacket
    {
        [JsonPropertyName("pos")]
        public string? Pos { get; set; }

        [JsonPropertyName("flags")]
        public string? Flags { get; set; }
    }

    private sealed class FfprobeStream
    {
        [JsonPropertyName("codec_name")]
        public string? CodecName { get; set; }

        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }

        [JsonPropertyName("r_frame_rate")]
        public string? RFrameRate { get; set; }

        [JsonPropertyName("bit_rate")]
        public string? BitRate { get; set; }
    }

    private sealed class FfprobeFormat
    {
        [JsonPropertyName("format_name")]
        public string? FormatName { get; set; }

        [JsonPropertyName("bit_rate")]
        public string? BitRate { get; set; }
    }
}








