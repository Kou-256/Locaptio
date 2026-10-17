using System.Management;
namespace Locaptio.Core.AI;

public sealed record HardwareInfo(string Cpu, string[] Gpus, string[] NpuCandidates);
public static class HardwareDetector
{
    public static HardwareInfo Detect()
    {
        string[] Query(string query)
        {
            try
            {
                using var search = new ManagementObjectSearcher(query);
                using var results = search.Get();
                return results.Cast<ManagementObject>().Select(item => { using (item) return item["Name"]?.ToString() ?? "Unknown"; }).ToArray();
            }
            catch { return []; }
        }
        return new(Query("SELECT Name FROM Win32_Processor").FirstOrDefault() ?? "CPU",
            Query("SELECT Name FROM Win32_VideoController"),
            Query("SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%NPU%' OR Name LIKE '%Neural%' OR Name LIKE '%IPU%'")
                .Where(name => !name.Contains("Input", StringComparison.OrdinalIgnoreCase)).ToArray());
    }
}
