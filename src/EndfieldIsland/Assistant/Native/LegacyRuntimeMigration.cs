using System.Diagnostics;
using System.Management;

namespace EndfieldChargePlus.Assistant.Native;

internal static class LegacyRuntimeMigration
{
    // Upgrade cleanup is narrowly owned: never stop Node belonging to Daily or another app.
    internal static void StopOwnedService()
    {
        var root=NativeConfiguration.DefaultRoot;
        var executable=Path.Combine(root,"runtime","node.exe");var script=Path.Combine(root,"core","server.js");
        if(!File.Exists(executable)||!File.Exists(script))return;
        try {
            using var searcher=new ManagementObjectSearcher("SELECT ProcessId,ExecutablePath,CommandLine FROM Win32_Process WHERE Name='node.exe'");
            using var rows=searcher.Get();
            foreach(ManagementObject row in rows)using(row) {
                var path=Convert.ToString(row["ExecutablePath"]);var command=Convert.ToString(row["CommandLine"])??"";
                if(!string.Equals(path,executable,StringComparison.OrdinalIgnoreCase)||!command.Contains(script,StringComparison.OrdinalIgnoreCase))continue;
                using var process=Process.GetProcessById(Convert.ToInt32(row["ProcessId"]));process.Kill();process.WaitForExit(3000);
            }
        }catch{ /* A protected/unrelated process is left intact; it is never required by this app. */ }
    }
}
