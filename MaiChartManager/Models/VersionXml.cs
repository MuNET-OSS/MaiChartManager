using System.Text.Json.Serialization;
using System.Xml;
using Microsoft.VisualBasic.FileIO;

namespace MaiChartManager.Models;

public class VersionXml
{
    private XmlDocument xmlDoc;
    [JsonIgnore] public string GamePath { get; }
    public string AssetDir { get; }

    // name.str 在游戏里不会被用到
    public int Id { get; }
    public string FilePath => Path.Combine(GamePath, "Sinmai_Data", "StreamingAssets", AssetDir, $"musicVersion/MusicVersion{Id:000000}/MusicVersion.xml");

    public VersionXml(int id, string assetDir, string gamePath)
    {
        Id = id;
        GamePath = gamePath;
        AssetDir = assetDir;
        xmlDoc = new XmlDocument();
        xmlDoc.Load(FilePath);
    }

    public static VersionXml CreateNew(int id, string assetDir, string gamePath)
    {
        var dir = Path.Combine(gamePath, "Sinmai_Data", "StreamingAssets", assetDir, $"musicVersion/MusicVersion{id:000000}");
        Directory.CreateDirectory(dir);
        var text = $"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <MusicVersionData xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
                      <dataName>MusicVersion{id:000000}</dataName>
                      <name>
                        <id>{id}</id>
                        <str></str>
                      </name>
                      <genreName></genreName>
                      <genreNameTwoLine></genreNameTwoLine>
                      <version>22001</version>
                      <Color>
                        <R>110</R>
                        <G>217</G>
                        <B>67</B>
                      </Color>
                      <FileName>UI_CMN_TabTitle_MaimaiTitle_Ver{id}</FileName>
                      <priority>0</priority>
                      <disable>false</disable>
                    </MusicVersionData>
                    """;

        File.WriteAllText(Path.Combine(dir, "MusicVersion.xml"), text);
        return new VersionXml(id, assetDir, gamePath);
    }

    // 游戏识别分类的时候是对比 genreName 而不是 id，所以 genreName 不能重复
    public string GenreName
    {
        get => xmlDoc.SelectSingleNode("/MusicVersionData/genreName")?.InnerText;
        set => xmlDoc.SelectSingleNode("/MusicVersionData/genreName").InnerText = value;
    }

    public string GenreNameTwoLine
    {
        get => xmlDoc.SelectSingleNode("/MusicVersionData/genreNameTwoLine")?.InnerText;
        set => xmlDoc.SelectSingleNode("/MusicVersionData/genreNameTwoLine").InnerText = value;
    }

    public string FileName
    {
        get => xmlDoc.SelectSingleNode("/MusicVersionData/FileName")?.InnerText;
        set => xmlDoc.SelectSingleNode("/MusicVersionData/FileName").InnerText = value;
    }

    public int ColorR
    {
        get => int.Parse(xmlDoc.SelectSingleNode("/MusicVersionData/Color/R")?.InnerText);
        set => xmlDoc.SelectSingleNode("/MusicVersionData/Color/R").InnerText = value.ToString();
    }

    public int ColorG
    {
        get => int.Parse(xmlDoc.SelectSingleNode("/MusicVersionData/Color/G")?.InnerText);
        set => xmlDoc.SelectSingleNode("/MusicVersionData/Color/G").InnerText = value.ToString();
    }

    public int ColorB
    {
        get => int.Parse(xmlDoc.SelectSingleNode("/MusicVersionData/Color/B")?.InnerText);
        set => xmlDoc.SelectSingleNode("/MusicVersionData/Color/B").InnerText = value.ToString();
    }

    public int Version
    {
        get => int.Parse(xmlDoc.SelectSingleNode("/MusicVersionData/version")?.InnerText);
        set => xmlDoc.SelectSingleNode("/MusicVersionData/version").InnerText = value.ToString();
    }

    public void Save()
    {
        xmlDoc.Save(FilePath);
    }

    public void Delete()
    {
        FileSystem.DeleteDirectory(Path.Combine(GamePath, "Sinmai_Data", "StreamingAssets", AssetDir, $"musicVersion/MusicVersion{Id:000000}"), UIOption.AllDialogs, RecycleOption.SendToRecycleBin);
    }
}
