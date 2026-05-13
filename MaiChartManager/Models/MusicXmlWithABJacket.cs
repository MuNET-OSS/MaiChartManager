using System.Xml;
using Microsoft.VisualBasic.FileIO;

namespace MaiChartManager.Models;

public class MusicXmlWithABJacket(string filePath, string gamePath, string assetDir) : MusicXml(filePath, gamePath)
{
    public string? AssetBundleJacket => StaticSettings.AssetBundleJacketMap.GetValueOrDefault(NonDxId);
    public string? PseudoAssetBundleJacket => StaticSettings.PseudoAssetBundleJacketMap.GetValueOrDefault(NonDxId);
    public string AssetDir => assetDir;

    // 在 mod 里文件的 jacket 是优先的
    public new bool HasJacket => JacketPath is not null || AssetBundleJacket is not null || PseudoAssetBundleJacket is not null;
    public string? RealJacketPath => JacketPath ?? PseudoAssetBundleJacket ?? AssetBundleJacket;

    public new static MusicXmlWithABJacket CreateNew(int id, string gamePath, string assetDir)
    {
        var old = MusicXml.CreateNew(id, gamePath, assetDir);
        return new MusicXmlWithABJacket(old.FilePath, old.GamePath, assetDir);
    }

    public override void Refresh()
    {
        var notes = xmlDoc.SelectSingleNode("MusicData/notesData")?.ChildNodes;
        for (var i = 0; i < 6; i++)
        {
            Charts[i] = new Chart(notes[i], this);
        }

        foreach (var ext in jacketExtensions)
        {
            var path = Path.Combine(StaticSettings.ImageAssetsDir, $"{NonDxId:000000}.{ext}");
            if (File.Exists(path))
            {
                JacketPath = path;
                break;
            }
        }
    }

    public bool isAcbAwbExist => StaticSettings.AcbAwb.ContainsKey($"music{CueId:000000}.acb") && StaticSettings.AcbAwb.ContainsKey($"music{CueId:000000}.awb");

    public XmlDocument GetInnerXmlClone()
    {
        return (XmlDocument)xmlDoc.Clone();
    }

    public XmlDocument GetXmlWithoutEventsAndRights()
    {
        var clone = GetInnerXmlClone();
        var root = clone.SelectSingleNode("/MusicData");

        root.SelectSingleNode("rightsInfoName/id").InnerText = "0";
        root.SelectSingleNode("eventName/id").InnerText = "1";
        root.SelectSingleNode("eventName2/id").InnerText = "0";
        root.SelectSingleNode("subEventName/id").InnerText = "0";
        root.SelectSingleNode("lockType").InnerText = "0";
        root.SelectSingleNode("subLockType").InnerText = "0";

        return clone;
    }

    public int CueId
    {
        get => int.Parse(RootNode.SelectSingleNode("cueName/id")?.InnerText);
        set
        {
            Modified = true;
            var nonDxId = value % 10000;
            RootNode.SelectSingleNode("cueName/id").InnerText = nonDxId.ToString();
        }
    }

    public int MovieId
    {
        get => int.Parse(RootNode.SelectSingleNode("movieName/id")?.InnerText);
        set
        {
            Modified = true;
            var nonDxId = value % 10000;
            RootNode.SelectSingleNode("movieName/id").InnerText = nonDxId.ToString();
        }
    }

    public List<string> Problems
    {
        get
        {
            var res = new List<string>();
            if (!isAcbAwbExist)
            {
                res.Add("音频 ACB / AWB 缺失");
            }

            if (StaticSettings.GenreList.All(it => it.Id != GenreId))
            {
                res.Add("无效的流派");
            }

            if (StaticSettings.VersionList.All(it => it.Id != AddVersionId))
            {
                res.Add("无效的版本");
            }

            if (Charts.All(it => !it.Enable))
            {
                res.Add("没有启用的谱面");
            }

            if (GenreId == 107 && !Charts[0].Enable)
            {
                res.Add("宴会场必须启用绿谱");
            }

            if (GenreId == 107 && Id < 100000)
            {
                res.Add("宴会场歌曲的 ID 小于 100000");
            }

            return res;
        }
    }

    internal bool DeleteJacket()
    {
        bool shouldDelete = HasJacket && RealJacketPath?.Contains(@"\A000\", StringComparison.InvariantCultureIgnoreCase) == false;
        if (!shouldDelete) return false;
        var assetBundleJacket = this.AssetBundleJacket;
        
        Console.WriteLine("删除 jacket: " + RealJacketPath);
        try
        { 
            FileSystem.DeleteFile(RealJacketPath);
            if (RealJacketPath.EndsWith(".ab") && File.Exists(RealJacketPath + ".manifest")) // .ab的情况，要额外把manifest也干掉
                FileSystem.DeleteFile(RealJacketPath + ".manifest");
        }
        catch
        {
            Console.WriteLine($"删除 jacket 失败: {RealJacketPath}");
            return false;
        }
        JacketPath = null;
        StaticSettings.AssetBundleJacketMap.Remove(NonDxId);
        StaticSettings.PseudoAssetBundleJacketMap.Remove(NonDxId);

        // Issue #42: AB jackets come with a companion jacket_s/ui_jacket_xxx_s.ab, delete it too to avoid orphan
        if (assetBundleJacket is not null)
        {
            var abDir = Path.GetDirectoryName(assetBundleJacket);
            var parentDir = string.IsNullOrWhiteSpace(abDir) ? null : Path.GetDirectoryName(abDir);
            if (!string.IsNullOrWhiteSpace(parentDir))
            {
                var jacketSPath = Path.Combine(parentDir, "jacket_s",
                    Path.GetFileNameWithoutExtension(assetBundleJacket) + "_s" + Path.GetExtension(assetBundleJacket));
                if (File.Exists(jacketSPath) && !jacketSPath.Contains(@"\A000\", StringComparison.InvariantCultureIgnoreCase))
                {
                    Console.WriteLine("删除 jacket_s: " + jacketSPath);
                    try
                    {
                        FileSystem.DeleteFile(jacketSPath);
                        if (File.Exists(jacketSPath + ".manifest"))
                            FileSystem.DeleteFile(jacketSPath + ".manifest");
                    }
                    catch
                    {
                        Console.WriteLine($"删除 jacket_s 失败: {jacketSPath}");
                    }
                }
            }
        }
        return true;
    }
    
    public void Delete()
    {
        DeleteJacket();

        if (StaticSettings.AcbAwb.TryGetValue($"music{NonDxId:000000}.acb", out var acb) && acb?.Contains(@"\A000\", StringComparison.InvariantCultureIgnoreCase) == false)
        {
            Console.WriteLine("删除 acb: " + acb);
            try
            {
                FileSystem.DeleteFile(acb);
            }
            catch
            {
                Console.WriteLine($"删除 acb 失败: {acb}");
            }
        }

        if (StaticSettings.AcbAwb.TryGetValue($"music{NonDxId:000000}.awb", out var awb) && awb?.Contains(@"\A000\", StringComparison.InvariantCultureIgnoreCase) == false)
        {
            Console.WriteLine("删除 awb: " + awb);
            try
            {
                FileSystem.DeleteFile(awb);
            }
            catch
            {
                Console.WriteLine($"删除 awb 失败: {awb}");
            }
        }

        if (StaticSettings.MovieDataMap.TryGetValue(NonDxId, out var movieData) && movieData?.Contains(@"\A000\", StringComparison.InvariantCultureIgnoreCase) == false)
        {
            Console.WriteLine("删除 movieData: " + movieData);
            try
            {
                FileSystem.DeleteFile(movieData);
            }
            catch
            {
                Console.WriteLine($"删除 movieData 失败: {movieData}");
            }
        }

        try
        {
            Console.WriteLine("删除目录: " + Path.GetDirectoryName(FilePath));
            FileSystem.DeleteDirectory(Path.GetDirectoryName(FilePath), DeleteDirectoryOption.DeleteAllContents);
        }
        catch
        {
            Console.WriteLine($"删除目录失败: {Path.GetDirectoryName(FilePath)}");
        }
    }
}