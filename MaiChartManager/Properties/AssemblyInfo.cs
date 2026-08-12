using System.Reflection;
using System.Runtime.CompilerServices;
using MaiChartManager;

[assembly: AssemblyCompany("Clansty")]
[assembly: AssemblyProduct("MaiChartManager")]
[assembly: AssemblyTitle("MaiChartManager")]
// 版本号来自 AppMain.Version（AppMain.g.cs）：Windows 由 Packaging/Build.ps1 重写，
// Linux 由 Packaging/arch/PKGBUILD 重写，两端一致地从 git tag 派生。
[assembly: AssemblyFileVersion(AppMain.Version)]
[assembly: AssemblyInformationalVersion(AppMain.Version)]
[assembly: InternalsVisibleTo("MaiChartManager.Tests")]
[assembly: AssemblyVersion(AppMain.Version)]
#if WINDOWS
[assembly: System.Runtime.Versioning.TargetPlatformAttribute("Windows10.0.17763.0")]
[assembly: System.Runtime.Versioning.SupportedOSPlatformAttribute("Windows10.0.17134.0")]
#endif
