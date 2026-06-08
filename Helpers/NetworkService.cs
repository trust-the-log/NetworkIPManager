using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Text;
using NetworkIPManager.Models;

namespace NetworkIPManager.Helpers
{
    public static class NetworkService
    {
        public static List<NetworkAdapter> GetAdapters()
        {
            var result = new List<NetworkAdapter>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_NetworkAdapter WHERE NetConnectionID IS NOT NULL");

                foreach (ManagementObject obj in searcher.Get())
                {
                    var adapter = new NetworkAdapter
                    {
                        Name           = obj["NetConnectionID"]?.ToString() ?? "",
                        Description    = obj["Description"]?.ToString() ?? "",
                        InterfaceIndex = Convert.ToUInt32(obj["InterfaceIndex"] ?? 0),
                    };

                    var netStatus = Convert.ToUInt16(obj["NetConnectionStatus"] ?? 7);
                    adapter.Status = netStatus switch
                    {
                        2 => "Up",
                        7 => "Down",
                        0 => "Down",
                        _ => "Disabled"
                    };

                    using var cfgSearcher = new ManagementObjectSearcher(
                        $"SELECT * FROM Win32_NetworkAdapterConfiguration " +
                        $"WHERE InterfaceIndex={adapter.InterfaceIndex} AND IPEnabled=True");

                    var cfg = cfgSearcher.Get().OfType<ManagementObject>().FirstOrDefault();
                    if (cfg != null)
                    {
                        var ips     = cfg["IPAddress"]            as string[];
                        var subnets = cfg["IPSubnet"]             as string[];
                        var gws     = cfg["DefaultIPGateway"]     as string[];
                        var dns     = cfg["DNSServerSearchOrder"] as string[];
                        var dhcp    = Convert.ToBoolean(cfg["DHCPEnabled"] ?? false);

                        var ipv4 = ips?.FirstOrDefault(ip => !ip.Contains(':'));
                        var sub4 = subnets?.FirstOrDefault(s => s.Contains('.'));

                        adapter.IpAddress    = ipv4 ?? "";
                        adapter.PrefixLength = sub4 != null ? NetworkAdapter.MaskToPrefix(sub4) : 24;
                        adapter.Gateway      = gws?.FirstOrDefault() ?? "";
                        adapter.Dns1         = dns?.ElementAtOrDefault(0) ?? "";
                        adapter.Dns2         = dns?.ElementAtOrDefault(1) ?? "";
                        adapter.IsDhcp       = dhcp;
                        cfg.Dispose();
                    }

                    result.Add(adapter);
                }
            }
            catch
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    var ip4 = ni.GetIPProperties().UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily ==
                            System.Net.Sockets.AddressFamily.InterNetwork);
                    var gw = ni.GetIPProperties().GatewayAddresses
                        .FirstOrDefault()?.Address.ToString() ?? "";
                    var dnsAddrs = ni.GetIPProperties().DnsAddresses
                        .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        .Select(a => a.ToString()).ToList();

                    result.Add(new NetworkAdapter
                    {
                        Name         = ni.Name,
                        Description  = ni.Description,
                        Status       = ni.OperationalStatus == OperationalStatus.Up ? "Up" : "Down",
                        IpAddress    = ip4?.Address.ToString() ?? "",
                        PrefixLength = ip4?.PrefixLength ?? 24,
                        Gateway      = gw,
                        Dns1         = dnsAddrs.ElementAtOrDefault(0) ?? "",
                        Dns2         = dnsAddrs.ElementAtOrDefault(1) ?? "",
                        IsDhcp       = true,
                    });
                }
            }

            return result
                .OrderBy(a => a.Status == "Up" ? 0 : a.Status == "Down" ? 1 : 2)
                .ThenBy(a => a.Name)
                .ToList();
        }

        public static (bool success, string output) ApplyConfig(
            NetworkAdapter adapter, bool dhcp,
            string ip, string subnet, string gateway, string dns1, string dns2)
        {
            var script = dhcp
                ? BuildDhcpScript(adapter)
                : BuildStaticScript(adapter, ip, subnet, gateway, dns1, dns2);
            return RunElevatedPowerShell(script);
        }

        private static string BuildDhcpScript(NetworkAdapter a)
        {
            string name = a.Name.Replace("'", "''");
            return $@"
$ErrorActionPreference = 'Stop'
$name = '{name}'

# Imposta DHCP tramite netsh (non richiede privilegi admin)
$result = netsh interface ip set address name=$name source=dhcp 2>&1
if ($LASTEXITCODE -ne 0) {{
    Write-Error ""netsh DHCP failed: $result""
    exit 1
}}

# DNS automatico tramite netsh
netsh interface ip set dns name=$name source=dhcp | Out-Null

Write-Output ""OK: DHCP abilitato su '$name'""
";
        }

        private static string BuildStaticScript(NetworkAdapter a,
            string ip, string subnet, string gateway, string dns1, string dns2)
        {
            string name    = a.Name.Replace("'", "''");
            string mask    = subnet.Contains('.') ? subnet
                           : NetworkAdapter.PrefixToMask(int.Parse(subnet));
            string gwPart  = !string.IsNullOrWhiteSpace(gateway) ? gateway : "none";
            string dns1cmd = !string.IsNullOrWhiteSpace(dns1)
                ? $"netsh interface ip set dns name=$name static {dns1} primary"
                : "# DNS primario non specificato";
            string dns2cmd = !string.IsNullOrWhiteSpace(dns2)
                ? $"netsh interface ip add dns name=$name {dns2} index=2"
                : "# DNS secondario non specificato";

            return $@"
$ErrorActionPreference = 'Stop'
$name = '{name}'

# Imposta IP statico tramite netsh (non richiede privilegi admin)
$result = netsh interface ip set address name=$name static {ip} {mask} {gwPart} 2>&1
if ($LASTEXITCODE -ne 0) {{
    Write-Error ""netsh IP failed: $result""
    exit 1
}}

# DNS
{dns1cmd}
{dns2cmd}

Write-Output ""OK: IP statico {ip} / {mask} applicato su '$name'""
";
        }

        private static string BuildDnsArray(string dns1, string dns2)
        {
            if (!string.IsNullOrWhiteSpace(dns2))
                return $@"""{dns1}"",""{dns2}""";
            return $@"""{dns1}""";
        }

        public static (bool success, string output) RunElevatedPowerShell(string script)
        {
            var tmp = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"nim_{Guid.NewGuid():N}.ps1");
            System.IO.File.WriteAllText(tmp, script, Encoding.UTF8);

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = "powershell.exe",
                    Arguments              = $"-NoProfile -ExecutionPolicy Bypass -File \"{tmp}\"",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                };

                using var proc = Process.Start(psi)!;
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                try { System.IO.File.Delete(tmp); } catch { }

                bool ok = proc.ExitCode == 0;
                string output = ok
                    ? (stdout.Trim().Length > 0 ? stdout.Trim() : "Configurazione applicata.")
                    : (stderr.Trim().Length > 0 ? stderr.Trim() : $"ExitCode: {proc.ExitCode}");

                return (ok, output);
            }
            catch (Exception ex)
            {
                try { System.IO.File.Delete(tmp); } catch { }
                return (false, $"Errore avvio PowerShell: {ex.Message}");
            }
        }

        public static string GenerateScript(NetworkAdapter adapter, bool dhcp,
            string ip, string subnet, string gateway, string dns1, string dns2)
            => dhcp
                ? BuildDhcpScript(adapter)
                : BuildStaticScript(adapter, ip, subnet, gateway, dns1, dns2);

        public static bool IsAdministrator()
        {
            try
            {
                var id  = System.Security.Principal.WindowsIdentity.GetCurrent();
                var pri = new System.Security.Principal.WindowsPrincipal(id);
                return pri.IsInRole(
                    System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}
