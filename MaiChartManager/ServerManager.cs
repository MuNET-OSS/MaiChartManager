using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using idunno.Authentication.Basic;
using MaiChartManager.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.FileProviders;
using Pluralsight.Crypto;
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
        if (File.Exists(path))
        {
            return new X509Certificate2(path);
        }

        // ASP.NET 是不是不支持 ecc
        // var ecdsa = ECDsa.Create();
        // var req = new CertificateRequest("CN=MaiChartManager", ecdsa, HashAlgorithmName.SHA256);
        // req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        // req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        // req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], true));
        // req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
        // var builder = new SubjectAlternativeNameBuilder();
        // builder.AddDnsName("MaiChartManager");
        // req.CertificateExtensions.Add(builder.Build());
        //
        // var cert = req.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(5));
        using var ctx = new CryptContext();
        ctx.Open();

        var cert = ctx.CreateSelfSignedCertificate(
            new SelfSignedCertProperties
            {
                IsPrivateKeyExportable = true,
                KeyBitLength = 4096,
                Name = new X500DistinguishedName("CN=MaiChartManager"),
                ValidFrom = DateTime.Today.AddDays(-1),
                ValidTo = DateTime.Today.AddYears(5),
            });

        File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx));
        return cert;
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

    public static void StartApp(bool export, Action? onStart = null)
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
            .AddSingleton<StaticSettings>()
            .AddSingleton<MaidataImportService>()
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
            .AddJsonOptions(options =>
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

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
            app.Lifetime.ApplicationStarted.Register(onStart);

        app
            .UseExceptionHandler()
            .UseStatusCodePages()
            .UseSwagger()
            .UseSwaggerUI()
            .UseCors("qwq");
        if (export)
            app.UseFileServer(new FileServerOptions
            {
                FileProvider = new PhysicalFileProvider(Path.Combine(StaticSettings.exeDir, "wwwroot")),
            });
        app.MapControllers();
        Task.Run(app.Run);
    }
}