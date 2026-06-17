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

    public static void StartApp(bool export, Action<string>? onStart = null)
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseSentry((SentryAspNetCoreOptions o) =>
            {
                // Tells which project in Sentry to send events to:
                o.Dsn = "https://be7a9ae3a9a88f4660737b25894b3c20@sentry.c5y.moe/3";
                // Set TracesSampleRate to 1.0 to capture 100% of transactions for tracing.
                // We recommend adjusting this value in production.
                o.TracesSampleRate = 0.5;
            })
            .ConfigureKestrel(serverOptions =>
            {
                serverOptions.Limits.MaxRequestBodySize = null; // 允许无限制的请求体大小
            });

        builder.Services
            .AddHttpClient()
            .AddSingleton<StaticSettings>()
            .AddSingleton<LegacyMaidataImportService>()
            .AddSingleton<MaidataImportService>()
            .AddSingleton<MuModService>()
            .AddSingleton<ModConfigService>()
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
#else
        builder.Services.AddSingleton<MaiChartManager.Platform.IDesktopDialogService, MaiChartManager.Platform.Linux.HeadlessDialogService>();
        builder.Services.AddSingleton<MaiChartManager.Platform.ITaskbarProgress, MaiChartManager.Platform.Linux.NoopTaskbarProgress>();
        builder.Services.AddSingleton<MaiChartManager.Platform.IShellService, MaiChartManager.Platform.Linux.LinuxShellService>();
        builder.Services.AddSingleton<MaiChartManager.Platform.IAppShell, MaiChartManager.Platform.Linux.HeadlessAppShell>();
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
        if (export)
            app.UseFileServer(new FileServerOptions
            {
                FileProvider = new PhysicalFileProvider(StaticSettings.wwwroot),
            });
        app.MapControllers();
        Task.Run(app.Run);
    }

    public static string? GetLoopbackUrl()
    {
        var server = app?.Services.GetRequiredService<IServer>();
        var serverAddressesFeature = server?.Features.Get<IServerAddressesFeature>();

        if (serverAddressesFeature == null) return null;

        return serverAddressesFeature.Addresses.First();
    }
}
