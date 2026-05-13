using System.Security.Cryptography;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MaiChartManager.Utils;

public static class AssetBundleCreator
{
    private const int TextureFormatRgba32 = 4;

    private static string TemplateAbPath =>
        Path.Combine(StaticSettings.StreamingAssets, "A000", "AssetBundleImages", "jacket", "ui_jacket_000000.ab");

    public static void CreateTextureAssetBundle(
        string imagePath,
        string outputAbPath,
        string assetName,
        string containerPath,
        string abName,
        int? resizeWidth = null,
        int? resizeHeight = null)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"Image file not found: {imagePath}", imagePath);
        CreateTextureAssetBundle(File.ReadAllBytes(imagePath), outputAbPath, assetName, containerPath, abName, resizeWidth, resizeHeight);
    }

    public static void CreateTextureAssetBundle(
        ReadOnlySpan<byte> pngImageData,
        string outputAbPath,
        string assetName,
        string containerPath,
        string abName,
        int? resizeWidth = null,
        int? resizeHeight = null)
    {
        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(pngImageData);
        if (resizeWidth.HasValue && resizeHeight.HasValue)
            image.Mutate(x => x.Resize(resizeWidth.Value, resizeHeight.Value));

        image.Mutate(x => x.Flip(FlipMode.Vertical));

        int width = image.Width;
        int height = image.Height;
        var rgbaData = new byte[width * height * 4];
        image.CopyPixelDataTo(rgbaData);
        
        CreateTextureAssetBundle(rgbaData, width, height, outputAbPath, assetName, containerPath, abName);
    }

    private static void CreateTextureAssetBundle(
        byte[] rgbaData,
        int width,
        int height,
        string outputAbPath,
        string assetName,
        string containerPath,
        string abName)
    {
        if (!File.Exists(TemplateAbPath))
            throw new FileNotFoundException($"Template AB not found: {TemplateAbPath}", TemplateAbPath);

        var outputDir = Path.GetDirectoryName(outputAbPath);
        if (!string.IsNullOrWhiteSpace(outputDir))
            Directory.CreateDirectory(outputDir);

        // 加载模板 AB 并解包
        var am = new AssetsManager();
        var bunInst = am.LoadBundleFile(TemplateAbPath, true);
        var afileInst = am.LoadAssetsFileFromBundle(bunInst, 0, false);
        var afile = afileInst.file;

        // 修改 assets
        foreach (var info in afile.Metadata.AssetInfos)
        {
            var baseField = am.GetBaseField(afileInst, info);
            if (baseField.IsDummy) continue;

            if (baseField.TypeName == "Texture2D")
            {
                baseField["m_Name"].AsString = assetName;
                baseField["m_Width"].AsInt = width;
                baseField["m_Height"].AsInt = height;
                baseField["m_CompleteImageSize"].AsInt = rgbaData.Length;
                baseField["m_TextureFormat"].AsInt = TextureFormatRgba32;
                baseField["m_MipCount"].AsInt = 1;
                baseField["m_IsReadable"].AsBool = false;
                baseField["m_ImageCount"].AsInt = 1;
                baseField["m_TextureDimension"].AsInt = 2;
                baseField["m_ColorSpace"].AsInt = 1;

                // inline 图片数据，不使用外部 .resS
                baseField["image data"].AsByteArray = rgbaData;
                baseField["m_StreamData"]["offset"].AsUInt = 0;
                baseField["m_StreamData"]["size"].AsUInt = 0;
                baseField["m_StreamData"]["path"].AsString = string.Empty;

                info.SetNewData(baseField);
            }
            else if (baseField.TypeName == "AssetBundle")
            {
                baseField["m_Name"].AsString = abName;
                baseField["m_AssetBundleName"].AsString = abName;

                // 找到 Texture2D 的 pathId 用于 container 和 preload
                var texPathId = afile.Metadata.AssetInfos
                    .First(i => am.GetBaseField(afileInst, i).TypeName == "Texture2D").PathId;

                // 只保留 Texture2D 的 container 条目
                var containerTemplate = ValueBuilder.DefaultValueFieldFromArrayTemplate(
                    baseField["m_Container"]["Array"].TemplateField);
                containerTemplate["first"].AsString = containerPath;
                containerTemplate["second"]["preloadIndex"].AsInt = 0;
                containerTemplate["second"]["preloadSize"].AsInt = 1;
                containerTemplate["second"]["asset"]["m_FileID"].AsInt = 0;
                containerTemplate["second"]["asset"]["m_PathID"].AsLong = texPathId;
                baseField["m_Container"]["Array"].Children = [containerTemplate];
                baseField["m_Container"]["Array"].AsArray = new AssetTypeArrayInfo(1);

                // preload 也只保留 Texture2D
                var preloadTemplate = ValueBuilder.DefaultValueFieldFromArrayTemplate(
                    baseField["m_PreloadTable"]["Array"].TemplateField);
                preloadTemplate["m_FileID"].AsInt = 0;
                preloadTemplate["m_PathID"].AsLong = texPathId;
                baseField["m_PreloadTable"]["Array"].Children = [preloadTemplate];
                baseField["m_PreloadTable"]["Array"].AsArray = new AssetTypeArrayInfo(1);

                info.SetNewData(baseField);
            }
            else if (baseField.TypeName == "Sprite")
            {
                info.SetRemoved();
            }
        }

        // 写出修改后的 AssetsFile
        using var newAssetsStream = new MemoryStream();
        afile.Write(new AssetsFileWriter(newAssetsStream), 0);
        var newAssetsData = newAssetsStream.ToArray();

        // 构建未压缩 bundle → 回读 → 压缩
        var newCabName = $"MCM-{ComputeMd5Hex(abName)}";

        using var tempStream = new MemoryStream();
        WriteUncompressedBundle(newAssetsData, newCabName, tempStream);

        tempStream.Position = 0;
        var newBundle = new AssetBundleFile();
        newBundle.Read(new AssetsFileReader(tempStream));

        using var outStream = File.Create(outputAbPath);
        newBundle.Pack(new AssetsFileWriter(outStream), AssetBundleCompressionType.LZ4, false, null);
    }
    
    public static string CreateMusicJacketAssetBundles(ReadOnlySpan<byte> pngImageData, string AssetBundleImagesDir, int musicId)
    {
        var abiDir = Path.Combine(AssetBundleImagesDir, "jacket");
        var abiSDir = Path.Combine(AssetBundleImagesDir, "jacket_s");
        Directory.CreateDirectory(abiDir);
        Directory.CreateDirectory(abiSDir);
        
        string key = $"UI_Jacket_{musicId:000000}";
        
        CreateTextureAssetBundle(
            pngImageData,
            Path.Combine(abiDir, $"{key.ToLowerInvariant()}.ab"),
            key,
            $"assets/assetbundle/jacket/{key.ToLowerInvariant()}.png",
            $"jacket/{key.ToLowerInvariant()}.ab");

        CreateTextureAssetBundle(
            pngImageData,
            Path.Combine(abiSDir, $"{key.ToLowerInvariant()}_s.ab"),
            $"{key}_s",
            $"assets/assetbundle/jacket_s/{key.ToLowerInvariant()}_s.png",
            $"jacket_s/{key.ToLowerInvariant()}_s.ab",
            resizeWidth: 200,
            resizeHeight: 200);

        return Path.Combine(abiDir, $"{key}.ab");
    }
    
    // 读取输入的ab包当中的图片，然后用指定的assetName等参数重新打包。
    // 适用于歌曲改ID等的场景。
    public static void RepackTextureAssetBundle(
        string inputAbPath,
        string outputAbPath,
        string assetName,
        string containerPath,
        string abName)
    {
        if (!File.Exists(inputAbPath))
            throw new FileNotFoundException($"Input AB not found: {inputAbPath}", inputAbPath);

        var pngBytes = ImageConvert.GetTextureAsPngData(inputAbPath);
        if (pngBytes is null || pngBytes.Length == 0)
            throw new InvalidOperationException($"Could not decode jacket texture to PNG from: {inputAbPath}");
        CreateTextureAssetBundle(pngBytes, outputAbPath, assetName, containerPath, abName);
    }

    private static void WriteUncompressedBundle(byte[] assetsData, string cabName, Stream output)
    {
        byte[] blockDirInfo;
        using (var bdStream = new MemoryStream())
        {
            var bdWriter = new AssetsFileWriter(bdStream) { BigEndian = true };
            bdWriter.Write(new byte[16]);
            bdWriter.Write((int)1);
            bdWriter.Write((uint)assetsData.Length);
            bdWriter.Write((uint)assetsData.Length);
            bdWriter.Write((ushort)0);
            bdWriter.Write((int)1);
            bdWriter.Write((long)0);
            bdWriter.Write((long)assetsData.Length);
            bdWriter.Write((uint)4);
            bdWriter.WriteNullTerminated(cabName);
            bdWriter.Flush();
            blockDirInfo = bdStream.ToArray();
        }

        var writer = new AssetsFileWriter(output) { BigEndian = true };
        writer.WriteNullTerminated("UnityFS");
        writer.Write((uint)6);
        writer.WriteNullTerminated("5.x.x");
        writer.WriteNullTerminated("2018.4.7f1");
        var sizePos = output.Position;
        writer.Write((long)0);
        writer.Write((uint)blockDirInfo.Length);
        writer.Write((uint)blockDirInfo.Length);
        writer.Write((uint)0x40);
        writer.Write(blockDirInfo);
        writer.Write(assetsData);
        writer.Flush();

        var totalSize = output.Position;
        output.Position = sizePos;
        writer.Write((long)totalSize);
        output.Position = totalSize;
    }

    private static string ComputeMd5Hex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = MD5.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
