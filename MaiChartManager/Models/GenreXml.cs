using System.Text.Json.Serialization;
using System.Xml;

namespace MaiChartManager.Models;

public class GenreXml
{
    private XmlDocument xmlDoc;
    [JsonIgnore] public string GamePath { get; }
    public string AssetDir { get; }

    // name.str 在游戏里不会被用到
    public int Id { get; }
    public string FilePath => MaiChartManager.Utils.PathUtils.ResolveIgnoreCase(GamePath, "Sinmai_Data", "StreamingAssets", AssetDir, "musicGenre", $"musicgenre{Id:000000}", "MusicGenre.xml");

    public GenreXml(int id, string assetDir, string gamePath)
    {
        Id = id;
        GamePath = gamePath;
        AssetDir = assetDir;
        xmlDoc = new XmlDocument();
        xmlDoc.Load(FilePath);
    }

    public static GenreXml CreateNew(int id, string assetDir, string gamePath)
    {
        var dir = MaiChartManager.Utils.PathUtils.ResolveIgnoreCase(gamePath, "Sinmai_Data", "StreamingAssets", assetDir, "musicGenre", $"musicgenre{id:000000}");
        Directory.CreateDirectory(dir);
        var text = $"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <MusicGenreData xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
                      <dataName>musicgenre{id:000000}</dataName>
                      <name>
                        <id>{id}</id>
                        <str></str>
                      </name>
                      <genreName></genreName>
                      <genreNameTwoLine></genreNameTwoLine>
                      <Color>
                        <R>110</R>
                    <G>217</G>
                    <B>67</B>
                      </Color>
                      <FileName>UI_CMN_TabTitle_{id}</FileName>
                      <priority>0</priority>
                      <disable>false</disable>
                    </MusicGenreData>
                    """;

        File.WriteAllText(Path.Combine(dir, "MusicGenre.xml"), text);
        return new GenreXml(id, assetDir, gamePath);
    }

    // 游戏识别分类的时候是对比 genreName 而不是 id，所以 genreName 不能重复
    public string GenreName
    {
        get => xmlDoc.SelectSingleNode("/MusicGenreData/genreName")?.InnerText;
        set => xmlDoc.SelectSingleNode("/MusicGenreData/genreName").InnerText = value;
    }

    public string GenreNameTwoLine
    {
        get => xmlDoc.SelectSingleNode("/MusicGenreData/genreNameTwoLine")?.InnerText;
        set => xmlDoc.SelectSingleNode("/MusicGenreData/genreNameTwoLine").InnerText = value;
    }

    public string FileName
    {
        get => xmlDoc.SelectSingleNode("/MusicGenreData/FileName")?.InnerText;
        set => xmlDoc.SelectSingleNode("/MusicGenreData/FileName").InnerText = value;
    }

    public int ColorR
    {
        get => int.Parse(xmlDoc.SelectSingleNode("/MusicGenreData/Color/R")?.InnerText);
        set => xmlDoc.SelectSingleNode("/MusicGenreData/Color/R").InnerText = value.ToString();
    }

    public int ColorG
    {
        get => int.Parse(xmlDoc.SelectSingleNode("/MusicGenreData/Color/G")?.InnerText);
        set => xmlDoc.SelectSingleNode("/MusicGenreData/Color/G").InnerText = value.ToString();
    }

    public int ColorB
    {
        get => int.Parse(xmlDoc.SelectSingleNode("/MusicGenreData/Color/B")?.InnerText);
        set => xmlDoc.SelectSingleNode("/MusicGenreData/Color/B").InnerText = value.ToString();
    }

    public void Save()
    {
        xmlDoc.Save(FilePath);
    }

    public void Delete()
    {
        // 跨平台删除（Windows 进回收站，Linux 直接删除）
        MaiChartManager.Platform.PlatformFile.DeleteDirectory(MaiChartManager.Utils.PathUtils.ResolveIgnoreCase(GamePath, "Sinmai_Data", "StreamingAssets", AssetDir, "musicGenre", $"musicgenre{Id:000000}"));
    }
}
