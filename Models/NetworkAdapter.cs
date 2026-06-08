namespace NetworkIPManager.Models
{
    public class NetworkAdapter
    {
        public string Name           { get; set; } = "";
        public string Description    { get; set; } = "";
        public string Status         { get; set; } = "";
        public string IpAddress      { get; set; } = "";
        public int    PrefixLength   { get; set; } = 24;
        public string Gateway        { get; set; } = "";
        public string Dns1           { get; set; } = "";
        public string Dns2           { get; set; } = "";
        public bool   IsDhcp         { get; set; } = true;
        public uint   InterfaceIndex { get; set; }

        public string SubnetMask => PrefixToMask(PrefixLength);

        public string StatusDisplay
        {
            get
            {
                switch (Status)
                {
                    case "Up":       return "Connessa";
                    case "Down":     return "Disconnessa";
                    case "Disabled": return "Disabilitata";
                    default:         return Status;
                }
            }
        }

        public string Icon
        {
            get
            {
                var n = Name.ToLower();
                if (n.Contains("wi-fi") || n.Contains("wireless") || n.Contains("wlan")) return "📶";
                if (n.Contains("veth")  || n.Contains("hyper"))                          return "🔷";
                if (n.Contains("vpn")   || n.Contains("tunnel"))                         return "🔐";
                if (n.Contains("blue")  || n.Contains("bt"))                             return "🔵";
                if (n.Contains("loop"))                                                  return "🔄";
                return "🔌";
            }
        }

        public static string PrefixToMask(int prefix)
        {
            uint mask = prefix == 0 ? 0 : (0xFFFFFFFF << (32 - prefix));
            return $"{(mask >> 24) & 0xFF}.{(mask >> 16) & 0xFF}.{(mask >> 8) & 0xFF}.{mask & 0xFF}";
        }

        public static int MaskToPrefix(string mask)
        {
            if (string.IsNullOrWhiteSpace(mask)) return 24;
            try
            {
                var parts = mask.Split('.');
                uint bits = 0;
                foreach (var p in parts)
                    bits = (bits << 8) | uint.Parse(p);
                int count = 0;
                while ((bits & 0x80000000) != 0) { count++; bits <<= 1; }
                return count;
            }
            catch { return 24; }
        }
    }
}
