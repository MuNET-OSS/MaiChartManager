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

            var tex = new TextureFile();
            tex.m_Width = baseField["m_Width"].AsInt;
            tex.m_Height = baseField["m_Height"].AsInt;
            tex.m_TextureFormat = baseField["m_TextureFormat"].AsInt;
            tex.pictureData = baseField["image data"].AsByteArray;
            tex.m_StreamData.path = baseField["m_StreamData"]["path"].AsString;
            tex.m_StreamData.offset = baseField["m_StreamData"]["offset"].AsULong;
            tex.m_StreamData.size = baseField["m_StreamData"]["size"].AsUInt;

            // If picture data is in an external .resS, fill it from the bundle directory
            if (tex.pictureData is null || tex.pictureData.Length == 0)
            {
                tex.pictureData = tex.FillPictureData(Path.GetDirectoryName(inputAbPath) ?? ".");
            }

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
