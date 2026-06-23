using System.Text.Json;
using System.Text.Json.Serialization;

namespace MaiChartManager;

public enum MovieCodec
{
    ForceH264 = 0,
    ForceVP9 = 1
}

public class Config
{
    public bool Export { get; set; } = false;
    public string GamePath { get; set; } = "";
    public string OfflineKey { get; set; } = "";
    public bool UseAuth { get; set; } = false;
    public string AuthUsername { get; set; } = "";
    public string AuthPassword { get; set; } = "";
    public HashSet<string> HistoryPath { get; set; } = [];
    public string? Locale { get; set; } = null;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MovieCodec MovieCodec { get; set; } = MovieCodec.ForceVP9;
    public bool Yuv420p { get; set; } = true;
    public bool NoScale { get; set; } = false;
    // 强制视频走软件编码：硬件 H264 产物若游戏不认，改 config.json 设为 true 即可一键退回软件。
    public bool ForceSoftwareVideo { get; set; } = false;
    public bool IgnoreLevel { get; set; } = false;
    public bool DisableBga { get; set; } = false;
    public bool UseLegacyMaiLib { get; set; } = false;
    public bool ConvertJacketToAssetBundle { get; set; } = true;
    public int UiZoom { get; set; } = 0;

    // 记住上次文件夹选择对话框选中的目录，下次打开时从这里开始（而不是每次都回到 Documents）。
    public string? LastDialogFolder { get; set; } = null;

    public void Save()
    {
        var json = JsonSerializer.Serialize(this);
        File.WriteAllText(Path.Combine(StaticSettings.appData, "config.json"), json);
    }
}