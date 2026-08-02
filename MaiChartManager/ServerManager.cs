using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using idunno.Authentication.Basic;
using MaiChartManager.Controllers.Charts.Services;
using MaiChartManager.Controllers.Mod;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.FileProviders;
using Sentry.AspNetCore;

namespace MaiChartManager;

public static class ServerManager
{
    public static WebApplication? app;

    public static async Task StopAsync()
    {
        if (app == null) return;
        await app.StopAsync();
        await app.DisposeAsync();
        app = null;
    }

    public static bool IsRunning => app != null;

    private static X509Certificate2 GetCert()
    {
        var path = Path.Combine(StaticSettings.appData, "cert.pfx");
        if (File.Exists(path)) return X509CertificateLoader.LoadPkcs12FromFile(path, null);

        using var rsa = System.Security.Cryptography.RSA.Create(4096);
        var req = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=MaiChartManager", rsa,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        var cert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(5));
        var pfx = cert.Export(X509ContentType.Pfx);
        File.WriteAllBytes(path, pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, null);
    }

    private static bool IsPortAvailable(int port)
    {
        var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
        var tcpConnInfoArray = ipGlobalProperties.GetActiveTcpConnections();
        Console.WriteLine(string.Join(", ", tcpConnInfoArray.Select(tcpi => tcpi.LocalEndPoint.Port.ToString())));
        foreach (var tcpi in tcpConnInfoArray)
        {
            if (tcpi.LocalEndPoint.Port == port)
            {
                return false;
            }
        }

        return true;
    }

    private static int GetAvailablePort()
    {
        var port = 49182;
        while (!IsPortAvailable(port))
        {
            port++;
        }

        return port;
    }

    // serveSpa：在 loopback 上伺服 wwwroot 里的 Vue SPA（用于 Photino 桌面宿主），
    // 但不开放 LAN 端口。放在 onStart 之后以保持现有位置参数调用的兼容性。
    public static Task StartApp(bool export, Action<string>? onStart = null, bool serveSpa = false)
    {
        // ContentRoot 必须显式指定为应用自身目录：WebApplication 默认用当前工作目录(cwd)，
        // 而桌面宿主常从用户 HOME 启动，host 启动时会对 ContentRoot 做文件监视/扫描，
        // HOME 下海量文件会让 CreateBuilder 卡上几十秒。指向 exeDir 即可（wwwroot 伺服
        // 走独立 PhysicalFileProvider，不受 ContentRoot 影响）。
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = StaticSettings.exeDir,
        });
        builder.WebHost.UseSentry((SentryAspNetCoreOptions o) =>
            {
                // 指定 Sentry 项目，将事件发送到对应的项目：
                o.Dsn = "https://be7a9ae3a9a88f4660737b25894b3c20@sentry.c5y.moe/3";
                // 将 TracesSampleRate 设为 1.0 可捕获 100% 的事务用于追踪。
                // 建议在生产环境中适当调低该值。
                o.TracesSampleRate = 0.5;
            })
            .ConfigureKestrel(serverOptions =>
            {
                serverOptions.Limits.MaxRequestBodySize = null; // 允许无限制的请求体大小
            });

        builder.Services
            .AddHttpClient()
            .AddSingleton<StaticSettings>()
            .AddSingleton<MaidataImportService>()
            .AddSingleton<MuModService>()
            .AddSingleton<ModConfigService>()
            .AddSingleton<MaiChartManager.Services.ResourceJunctionService>()
            .AddEndpointsApiExplorer()
            .AddSwaggerGen(options => { options.CustomSchemaIds(type => type.Name == "Config" ? type.FullName : type.Name); })
            .Configure<FormOptions>(x =>
            {
                x.ValueLengthLimit = int.MaxValue;
                x.MultipartBodyLengthLimit = long.MaxValue; // In case of multipart
            })
            .AddCors(options => options.AddPolicy("qwq", policy =>
            {
                policy.WithOrigins("https://mcm.invalid")
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            }))
            .AddProblemDetails(options =>
                options.CustomizeProblemDetails = (context) =>
                {
                    context.ProblemDetails.Title = context.Exception?.GetType()?.FullName ?? Locale.UnknownError;
                    context.ProblemDetails.Detail = context.Exception?.Message ?? Locale.UnknownError;
                }
            )
            .AddControllers()
            .AddApplicationPart(typeof(ServerManager).Assembly)
            .AddJsonOptions(options =>
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

#if WINDOWS
        builder.Services.AddSingleton<MaiChartManager.Platform.IDesktopDialogService, MaiChartManager.Platform.Windows.WinFormsDialogService>();
        builder.Services.AddSingleton<MaiChartManager.Platform.ITaskbarProgress, MaiChartManager.Platform.Windows.WindowsTaskbarProgress>();
        builder.Services.AddSingleton<MaiChartManager.Platform.IShellService, MaiChartManager.Platform.Windows.WindowsShellService>();
        builder.Services.AddSingleton<MaiChartManager.Platform.IAppShell, MaiChartManager.Platform.Windows.WindowsAppShell>();
        builder.Services.AddSingleton<MaiChartManager.Platform.IProgressController, MaiChartManager.Platform.Windows.WindowsProgressController>();
#else
        // 使用 Photino 原生对话框（替换原 HeadlessDialogService 占位实现），让 OOBE 选目录可用。
        builder.Services.AddSingleton<MaiChartManager.Platform.IDesktopDialogService, MaiChartManager.Platform.Linux.PhotinoDialogService>();
        builder.Services.AddSingleton<MaiChartManager.Platform.ITaskbarProgress, MaiChartManager.Platform.Linux.NoopTaskbarProgress>();
        builder.Services.AddSingleton<MaiChartManager.Platform.IShellService, MaiChartManager.Platform.Linux.LinuxShellService>();
        builder.Services.AddSingleton<MaiChartManager.Platform.IAppShell, MaiChartManager.Platform.Linux.PhotinoAppShell>();
        builder.Services.AddSingleton<MaiChartManager.Platform.IProgressController, MaiChartManager.Platform.Linux.HeadlessProgressController>();
#endif

        if (StaticSettings.Config.UseAuth)
        {
            builder.Services.AddAuthentication(BasicAuthenticationDefaults.AuthenticationScheme)
                .AddBasic(options =>
                {
                    options.Events = new BasicAuthenticationEvents
                    {
                        OnValidateCredentials = context =>
                        {
                            if (context.Username == StaticSettings.Config.AuthUsername && context.Password == StaticSettings.Config.AuthPassword)
                            {
                                context.Principal = new ClaimsPrincipal(new ClaimsIdentity([], context.Scheme.Name));
                                context.Success();
                            }

                            return Task.CompletedTask;
                        }
                    };
                });
            builder.Services.AddAuthorization();
        }

# if !DEBUG
        builder.WebHost.ConfigureKestrel((context, serverOptions) =>
        {
            serverOptions.Listen(IPAddress.Loopback, 0);
            if (export)
            {
                serverOptions.Listen(IPAddress.Any, 5001, listenOptions =>
                {
                    listenOptions.UseHttps(new HttpsConnectionAdapterOptions()
                    {
                        ServerCertificate = GetCert()
                    });
                });
            }
        });
# endif

        app = builder.Build();
        if (StaticSettings.Config.UseAuth)
        {
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseMiddleware<AuthenticationMiddleware>();
        }

        app.Lifetime.ApplicationStarted.Register(() => { app.Services.GetService<StaticSettings>(); });

        if (onStart != null)
            app.Lifetime.ApplicationStarted.Register(() =>
            {
                onStart(GetLoopbackUrl() ?? throw new InvalidOperationException("Loopback URL is null"));
            });

        app
            .UseExceptionHandler()
            .UseStatusCodePages()
            .UseSwagger()
            .UseSwaggerUI()
            .UseCors("qwq");
        // 当 export 或 serveSpa 时都伺服 SPA：export 是导出场景，serveSpa 是 Photino 桌面宿主场景
        if (export || serveSpa)
            app.UseFileServer(new FileServerOptions
            {
                FileProvider = new PhysicalFileProvider(StaticSettings.wwwroot),
            });
        app.MapControllers();
        return Task.Run(app.Run);
    }

    public static string? GetLoopbackUrl()
    {
        var server = app?.Services.GetRequiredService<IServer>();
        var serverAddressesFeature = server?.Features.Get<IServerAddressesFeature>();

        if (serverAddressesFeature == null) return null;

        return serverAddressesFeature.Addresses.First();
    }
}
