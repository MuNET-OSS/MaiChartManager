using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using MaiChartManager.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;

namespace MaiChartManager.Utils;

public static class ImageConvert
{
    public static byte[]? GetMusicJacketPngData(this MusicXmlWithABJacket? music)
    {
        if (music == null) return null;
        if (File.Exists(music.JacketPath)) return File.ReadAllBytes(music.JacketPath);
        if (File.Exists(music.PseudoAssetBundleJacket)) return File.ReadAllBytes(music.PseudoAssetBundleJacket);
        if (music.AssetBundleJacket is null) return null;
        return GetTextureAsPngData(music.AssetBundleJacket);
    }

    public static byte[]? GetTextureAsPngData(string inputAbPath)
    {
        var am = new AssetsManager();
        var bunInst = am.LoadBundleFile(inputAbPath, true);
        var afileInst = am.LoadAssetsFileFromBundle(bunInst, 0, false);

        foreach (var info in afileInst.file.Metadata.AssetInfos)
        {
            var baseField = am.GetBaseField(afileInst, info);
            if (baseField.IsDummy || baseField.TypeName != "Texture2D") continue;

            var tex = TextureFile.ReadTextureFile(baseField);
            // 从 bundle 内解析贴图像素数据：同时覆盖 inline "image data" 与 bundle 内部的 .resS streamData。
            // 游戏原始封面的像素数据存在 bundle 内部的 .resS 里，必须用 bundle 解析，
            // 不能只读文件系统目录（之前用 FillPictureData(目录) 会导致原始封面解析失败、接口返回 404）。
            tex.SetPictureDataFromBundle(bunInst);
            if (tex.pictureData is null || tex.pictureData.Length == 0) return null;

            var bgra = TextureFile.DecodeManagedData(
                tex.pictureData, (TextureFormat)tex.m_TextureFormat, tex.m_Width, tex.m_Height, true);
            if (bgra is null || bgra.Length == 0) return null;

            using var image = Image.LoadPixelData<Bgra32>(bgra, tex.m_Width, tex.m_Height);
            image.Mutate(x => x.Flip(FlipMode.Vertical));
            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
        }
        return null;
    }
}
