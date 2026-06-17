using System.Reflection;
#if WINDOWS
using MaiChartManager;
#endif

[assembly: AssemblyCompany("Clansty")]
#if WINDOWS
[assembly: AssemblyFileVersion(AppMain.Version)]
[assembly: AssemblyInformationalVersion(AppMain.Version)]
#endif
[assembly: AssemblyProduct("MaiChartManager")]
[assembly: AssemblyTitle("MaiChartManager")]
#if WINDOWS
[assembly: AssemblyVersion(AppMain.Version)]
[assembly: System.Runtime.Versioning.TargetPlatformAttribute("Windows10.0.17763.0")]
[assembly: System.Runtime.Versioning.SupportedOSPlatformAttribute("Windows10.0.17134.0")]
#endif