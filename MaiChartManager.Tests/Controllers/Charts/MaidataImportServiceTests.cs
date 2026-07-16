using MaiChartManager.Controllers.Charts.Services;
using MaiChartManager.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace MaiChartManager.Tests.Controllers.Charts;

public sealed class MaidataImportServiceTests : IDisposable
{
    private readonly string _gamePath = Path.Combine(Path.GetTempPath(), $"mcm-utage-{Guid.NewGuid():N}");
    private readonly MaidataImportService _service = new(NullLogger<MaidataImportService>.Instance);

    [Fact]
    public void 单谱面宴谱自动映射到Basic()
    {
        var music = CreateMusic(100001);

        var result = Import(music, OneChartMaidata);

        Assert.False(result.Fatal);
        Assert.True(music.Charts[0].Enable);
        Assert.Equal(1, music.Charts[0].MaxNotes);
        Assert.Equal(0, music.UtagePlayStyle);
        Assert.True(File.Exists(GetMusicFile(100001, "100001_00.ma2")));
    }

    [Fact]
    public void 多谱面宴谱可选择一份映射到Basic()
    {
        var music = CreateMusic(100002);
        var options = new UtageImportOptions(false, 3, null, null);

        var result = Import(music, TwoChartMaidata, options);

        Assert.False(result.Fatal);
        Assert.Equal(2, music.Charts[0].MaxNotes);
        Assert.Equal(0, music.UtagePlayStyle);
        Assert.True(File.Exists(GetMusicFile(100002, "100002_00.ma2")));
        Assert.False(File.Exists(GetMusicFile(100002, "100002_00_L.ma2")));
    }

    [Fact]
    public void 多谱面宴谱可分别映射左右谱面()
    {
        var music = CreateMusic(100003);
        var options = new UtageImportOptions(true, null, 3, 2);

        var result = Import(music, TwoChartMaidata, options);

        Assert.False(result.Fatal);
        Assert.Equal(1, music.UtagePlayStyle);
        Assert.Equal(3, music.Charts[0].MaxNotes);
        Assert.Equal(13, music.Charts[0].Level);
        Assert.True(File.Exists(GetMusicFile(100003, "100003_00_L.ma2")));
        Assert.True(File.Exists(GetMusicFile(100003, "100003_00_R.ma2")));
        Assert.False(File.Exists(GetMusicFile(100003, "100003_00.ma2")));
    }

    [Fact]
    public void 双人谱面左右不能映射同一份谱面()
    {
        var music = CreateMusic(100004);
        var options = new UtageImportOptions(true, null, 2, 2);

        var result = Import(music, TwoChartMaidata, options);

        Assert.True(result.Fatal);
        Assert.False(File.Exists(GetMusicFile(100004, "100004_00_L.ma2")));
        Assert.False(File.Exists(GetMusicFile(100004, "100004_00_R.ma2")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_gamePath)) Directory.Delete(_gamePath, true);
    }

    private MusicXml CreateMusic(int id) => MusicXml.CreateNew(id, _gamePath, "A001");

    private ImportChartResult Import(MusicXml music, string maidata, UtageImportOptions? options = null)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(maidata);
        using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "file", "maidata.txt");
        return _service.ImportMaidata(music, file, ShiftMethod.NoShift, false, false, utageOptions: options);
    }

    private string GetMusicFile(int id, string fileName) => Path.Combine(
        _gamePath,
        "Sinmai_Data",
        "StreamingAssets",
        "A001",
        "music",
        $"music{id:000000}",
        fileName);

    private const string OneChartMaidata = """
        &title=单谱面
        &artist=测试
        &wholebpm=120
        &first=0
        &lv_2=12
        &inote_2=(120){4}1,E
        """;

    private const string TwoChartMaidata = """
        &title=双谱面
        &artist=测试
        &wholebpm=120
        &first=0
        &lv_2=12
        &inote_2=(120){4}1,E
        &lv_3=13
        &inote_3=(120){4}1/2,E
        """;
}
