using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.VisualBasic;

namespace MaiChartManager;

/// <summary>
/// Legacy 启动器窗口，现在应该只有在没有 webview2 的时候才使用
/// </summary>
public partial class Launcher : Form
{
    public Launcher()
    {
        InitializeComponent();
        label3.Text = $@"v{Application.ProductVersion}";
# if CRACK
        label3.Text += " 内部版本";
# endif
        checkBox1.Checked = StaticSettings.Config.Export;
        textBox1.Text = StaticSettings.Config.GamePath;
        checkBoxLanAuth.Checked = StaticSettings.Config.UseAuth;
        textBoxLanAuthUser.Text = StaticSettings.Config.AuthUsername;
        textBoxLanAuthPass.Text = StaticSettings.Config.AuthPassword;
        textBox1.Items.AddRange(StaticSettings.Config.HistoryPath.ToArray());
        CheckStartupStatus();
        PositionOnScreen();
# if DEBUG
        checkBox1.Checked = true;
        StaticSettings.Config.Export = true;
        textBox1.Text = @"Z:\mnt\Arcade\SDEZ165";
        StartClicked(null, null);
        Visible = false;
# endif
        comboBox1.SelectedIndex = StaticSettings.CurrentLocale switch
        {
            "zh" => 0,
            "zh-TW" => 1,
            "en" => 2,
            _ => 2,
        };
        if (!AppMain.IsFromStartup)
        {
# if !DEBUG
            Visible = true;
# endif
            IapManager.BindToForm(this);
            return;
        }

        // 开机自启
        Visible = false;
        notifyIcon1.Visible = true;
        checkBox1.Checked = true;
        StaticSettings.Config.Export = true;
        StartClicked(null, null);
    }

    private void PositionOnScreen()
    {
        StartPosition = FormStartPosition.Manual;
        var landscape = Screen.AllScreens.FirstOrDefault(s => s.WorkingArea.Width >= s.WorkingArea.Height);
        if (landscape != null)
        {
            var area = landscape.WorkingArea;
            Location = new Point(
                area.X + (area.Width - Width) / 2,
                area.Y + (area.Height - Height) / 2
            );
            return;
        }
        var portrait = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        var pArea = portrait.WorkingArea;
        var squareSize = pArea.Width;
        var squareTop = pArea.Bottom - squareSize;
        Location = new Point(
            pArea.X + (squareSize - Width) / 2,
            squareTop + (squareSize - Height) / 2
        );
    }

    private async Task CheckStartupStatus()
    {
        var startupTask = await StartupTask.GetAsync("MaiChartManagerStartupId");
        switch (startupTask.State)
        {
            case StartupTaskState.Disabled:
                checkBox_startup.Checked = false;
                break;
            case StartupTaskState.Enabled:
                checkBox_startup.Checked = true;
                break;
            case StartupTaskState.DisabledByUser:
            case StartupTaskState.DisabledByPolicy:
                checkBox_startup.Enabled = false;
                checkBox_startup.Checked = false;
                break;
            case StartupTaskState.EnabledByPolicy: // ??
                checkBox_startup.Enabled = false;
                checkBox_startup.Checked = true;
                break;
        }
    }

    private void button1_Click(object sender, EventArgs e)
    {
        var result = folderBrowserDialog1.ShowDialog();
        if (result != DialogResult.OK) return;
        textBox1.Text = folderBrowserDialog1.SelectedPath;
    }

    private string? loopbackUrl;


    [GeneratedRegex(@"[^\u0000-\u007F]")]
    private static partial Regex SpecialCharactersRegex();

    private static bool ContainsSpecialCharacters(string input)
    {
        return SpecialCharactersRegex().IsMatch(input);
    }

    private void StartClicked(object? sender, EventArgs? e)
    {
        if (button2.Text == Locale.LauncherStop)
        {
            button2.Text = Locale.LauncherStart;
            textBox1.Enabled = true;
            button1.Enabled = true;
            checkBox1.Enabled = true;
            checkBoxLanAuth.Enabled = true;
            textBoxLanAuthUser.Enabled = true;
            textBoxLanAuthPass.Enabled = true;
            label1.Text = "";
            ServerManager.StopAsync();
            return;
        }

        if (string.IsNullOrWhiteSpace(textBox1.Text)) return;
        if (!Path.Exists(textBox1.Text))
        {
            MessageBox.Show(Locale.PathNotExist);
            return;
        }

        StaticSettings.GamePath = textBox1.Text;
        if (!Directory.Exists(StaticSettings.StreamingAssets) && Directory.Exists(Path.Combine(StaticSettings.GamePath, "Package")))
        {
            StaticSettings.GamePath = Path.Combine(StaticSettings.GamePath, "Package");
        }
        if (!Directory.Exists(StaticSettings.StreamingAssets))
        {
            MessageBox.Show(Locale.PathNotGameDir);
            return;
        }

        if (ContainsSpecialCharacters(StaticSettings.GamePath))
        {
            MessageBox.Show(Locale.PathContainsSpecialChars, Locale.PathContainsSpecialCharsTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (!checkBox1.Checked && checkBox_startup.Checked)
        {
            checkBox_startup.Checked = false;
            checkBox_startup_Click(null, null);
        }

# if !DEBUG
        StaticSettings.Config.GamePath = textBox1.Text;
        StaticSettings.Config.HistoryPath.Add(textBox1.Text);
        StaticSettings.Config.UseAuth = checkBoxLanAuth.Checked;
        StaticSettings.Config.AuthUsername = textBoxLanAuthUser.Text;
        StaticSettings.Config.AuthPassword = textBoxLanAuthPass.Text;
        StaticSettings.Config.Save();
# endif

        textBox1.Enabled = false;
        button1.Enabled = false;
        checkBox1.Enabled = false;
        checkBoxLanAuth.Enabled = false;
        textBoxLanAuthUser.Enabled = false;
        textBoxLanAuthPass.Enabled = false;
        button2.Text = Locale.LauncherStop;

        ServerManager.StartApp(checkBox1.Checked, (url) =>
        {
            loopbackUrl = url;

            // 本地模式
            if (checkBox1.Checked) return;
            AppLifecycleManager.ShowBrowser(loopbackUrl);
            AppMain.UiContext?.Post(_ => Dispose(), null);
        });

        if (!checkBox1.Checked) return;
        var localIp = Dns.GetHostAddresses(Dns.GetHostName()).First(it => it.AddressFamily == AddressFamily.InterNetwork);
        label1.Text = $@"https://{localIp}:5001";
    }

    private void button4_Click(object sender, EventArgs e)
    {
        Application.Exit();
    }

    private void label1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        AppLifecycleManager.ShowBrowser(loopbackUrl ?? throw new InvalidOperationException("loopbackUrl is null"));
    }

    private void Launcher_FormClosed(object sender, FormClosedEventArgs e)
    {
        Application.Exit();
    }

    private void checkBox1_CheckedChanged(object sender, EventArgs e)
    {
        StaticSettings.Config.Export = checkBox1.Checked;
        checkBox_startup.Visible = checkBox1.Checked;
        checkBoxLanAuth.Visible = checkBox1.Checked;
        if (!checkBox1.Checked)
        {
            checkBoxLanAuth.Checked = false;
        }
    }

    private async void checkBox_startup_Click(object? sender, EventArgs? e)
    {
        await SaveConfigFileAsync();
        var startupTask = await StartupTask.GetAsync("MaiChartManagerStartupId");
        if (checkBox_startup.Checked)
        {
            await startupTask.RequestEnableAsync();
        }
        else
        {
            startupTask.Disable();
        }
    }

    public void ShowWindow(object? sender = null, EventArgs? e = null)
    {
        Visible = true;
        WindowState = FormWindowState.Normal;
        notifyIcon1.Visible = false;
        Show();
        Focus();
    }

    private static async Task SaveConfigFileAsync()
    {
        StaticSettings.Config.Save();
    }

    private async void label3_Click(object sender, EventArgs e)
    {
        if ((ModifierKeys & Keys.Shift) != Keys.Shift) return;
        if (IapManager.License == IapManager.LicenseStatus.Active) return;

        var input = Interaction.InputBox(Locale.OfflineActivationPrompt, Locale.OfflineActivationTitle);
        if (string.IsNullOrWhiteSpace(input)) return;

        var verify = await OfflineReg.VerifyAsync(input);
        if (!verify.IsValid)
        {
            MessageBox.Show(Locale.ActivationCodeInvalid);
            return;
        }

        MessageBox.Show(Locale.ActivationSuccess);

        StaticSettings.Config.OfflineKey = input;
        await SaveConfigFileAsync();
        await IapManager.Init();
    }

    private void checkBoxLanAuth_CheckedChanged(object sender, EventArgs e)
    {
        textBoxLanAuthUser.Visible = checkBoxLanAuth.Checked;
        textBoxLanAuthPass.Visible = checkBoxLanAuth.Checked;
    }

    private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
    {
        switch (comboBox1.SelectedIndex)
        {
            case 0:
                AppMain.SetLocale("zh");
                break;
            case 1:
                AppMain.SetLocale("zh-TW");
                break;
            case 2:
                AppMain.SetLocale("en");
                break;
        }

        RefreshLocalizedTexts();
    }

    private void RefreshLocalizedTexts()
    {
        button1.Text = Locale.LauncherSelectGameDir;
        button4.Text = Locale.LauncherExit;
        label2.Text = Locale.LauncherGameDir;
        checkBox1.Text = Locale.LauncherOpenToLan;
        checkBox_startup.Text = Locale.LauncherStartup;
        checkBoxLanAuth.Text = Locale.LauncherNeedLogin;
        if (button2.Text == Locale.LauncherStop || button2.Text.Contains("Stop") || button2.Text.Contains("停止"))
        {
            button2.Text = Locale.LauncherStop;
        }
        else
        {
            button2.Text = Locale.LauncherStart;
        }
    }
}