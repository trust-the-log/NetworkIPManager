using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using NetworkIPManager.Helpers;
using NetworkIPManager.Models;

namespace NetworkIPManager.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute    = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter)    => _execute(parameter);

        public event EventHandler? CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        // ── Schede ─────────────────────────────────────────────────────────
        public ObservableCollection<NetworkAdapter> Adapters { get; } = new();

        private NetworkAdapter? _selected;
        public NetworkAdapter? SelectedAdapter
        {
            get => _selected;
            set
            {
                _selected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
                if (value != null) PreFill(value);
            }
        }
        public bool HasSelection => _selected != null;

        // ── Modalità ───────────────────────────────────────────────────────
        private bool _dhcpMode = true;
        public bool DhcpMode
        {
            get => _dhcpMode;
            set
            {
                _dhcpMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StaticMode));
            }
        }
        public bool StaticMode => !_dhcpMode;

        public void SetDhcpMode()   { DhcpMode = true;  UpdateScript(); }
        public void SetStaticMode() { DhcpMode = false; UpdateScript(); }

        // ── Campi IP ───────────────────────────────────────────────────────
        private string _ip = "", _subnet = "", _gateway = "", _dns1 = "", _dns2 = "";

        public string Ip
        {
            get => _ip;
            set { _ip = value; OnPropertyChanged(); UpdateScript(); }
        }
        public string Subnet
        {
            get => _subnet;
            set { _subnet = value; OnPropertyChanged(); UpdateScript(); }
        }
        public string Gateway
        {
            get => _gateway;
            set { _gateway = value; OnPropertyChanged(); UpdateScript(); }
        }
        public string Dns1
        {
            get => _dns1;
            set { _dns1 = value; OnPropertyChanged(); UpdateScript(); }
        }
        public string Dns2
        {
            get => _dns2;
            set { _dns2 = value; OnPropertyChanged(); UpdateScript(); }
        }

        // ── Script generato ────────────────────────────────────────────────
        private string _script = "// Seleziona una scheda per generare lo script";
        public string GeneratedScript
        {
            get => _script;
            set { _script = value; OnPropertyChanged(); }
        }

        // ── Log ────────────────────────────────────────────────────────────
        private string _log = "In attesa di operazioni...\n";
        public string Log
        {
            get => _log;
            set { _log = value; OnPropertyChanged(); }
        }

        // ── Stato ──────────────────────────────────────────────────────────
        private bool _busy;
        public bool Busy
        {
            get => _busy;
            set { _busy = value; OnPropertyChanged(); OnPropertyChanged(nameof(NotBusy)); }
        }
        public bool NotBusy => !_busy;

        private string _adminLabel = "";
        public string AdminLabel
        {
            get => _adminLabel;
            set { _adminLabel = value; OnPropertyChanged(); }
        }

        // ── Comandi ────────────────────────────────────────────────────────
        public ICommand RefreshCmd   { get; }
        public ICommand ApplyCmd     { get; }
        public ICommand CopyCmd      { get; }
        public ICommand SetDhcpCmd   { get; }
        public ICommand SetStaticCmd { get; }

        public MainViewModel()
        {
            RefreshCmd   = new RelayCommand(_ => LoadAdapters());
            ApplyCmd     = new RelayCommand(_ => ApplyConfig(), _ => HasSelection && NotBusy);
            CopyCmd      = new RelayCommand(_ => Clipboard.SetText(GeneratedScript));
            SetDhcpCmd   = new RelayCommand(_ => SetDhcpMode());
            SetStaticCmd = new RelayCommand(_ => SetStaticMode());

            AdminLabel = NetworkService.IsAdministrator()
                ? "✅  Eseguito come Amministratore"
                : "⚠️  Non admin — verrà richiesto UAC";

            LoadAdapters();
        }

        // ── Carica schede ──────────────────────────────────────────────────
        private async void LoadAdapters()
        {
            Busy = true;
            AddLog("INFO", "Rilevamento schede di rete...");
            try
            {
                var list = await Task.Run(() => NetworkService.GetAdapters());
                Adapters.Clear();
                foreach (var a in list) Adapters.Add(a);
                AddLog("OK", $"Trovate {list.Count} schede.");
            }
            catch (Exception ex)
            {
                AddLog("WARN", $"Errore rilevamento: {ex.Message}");
            }
            finally
            {
                Busy = false;
            }
        }

        // ── Precompila campi ───────────────────────────────────────────────
        private void PreFill(NetworkAdapter a)
        {
            DhcpMode = a.IsDhcp;
            Ip       = a.IpAddress;
            Subnet   = a.SubnetMask;
            Gateway  = a.Gateway;
            Dns1     = a.Dns1;
            Dns2     = a.Dns2;
            AddLog("INFO", $"Selezionata: {a.Name} ({a.Description})");
            UpdateScript();
        }

        // ── Aggiorna script preview ────────────────────────────────────────
        private void UpdateScript()
        {
            if (_selected == null) return;
            GeneratedScript = NetworkService.GenerateScript(
                _selected, DhcpMode, Ip, Subnet, Gateway, Dns1, Dns2);
        }

        // ── Validazione ────────────────────────────────────────────────────
        private static bool IsValidIp(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;
            var parts = ip.Trim().Split('.');
            if (parts.Length != 4) return false;
            foreach (var part in parts)
                if (!int.TryParse(part, out int n) || n < 0 || n > 255) return false;
            return true;
        }

        private static bool IsValidSubnet(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (int.TryParse(s, out int prefix)) return prefix >= 0 && prefix <= 32;
          
            return IsValidIp(s);
        }

        private bool ValidateStatic(out string error)
        {
            if (!IsValidIp(Ip.Trim()))
                { error = $"Indirizzo IP non valido: '{Ip}'"; return false; }
            if (!IsValidSubnet(Subnet.Trim()))
                { error = $"Subnet mask non valida: '{Subnet}'"; return false; }
            if (!string.IsNullOrWhiteSpace(Gateway) && !IsValidIp(Gateway.Trim()))
                { error = $"Gateway non valido: '{Gateway}'"; return false; }
            if (!string.IsNullOrWhiteSpace(Dns1) && !IsValidIp(Dns1.Trim()))
                { error = $"DNS primario non valido: '{Dns1}'"; return false; }
            if (!string.IsNullOrWhiteSpace(Dns2) && !IsValidIp(Dns2.Trim()))
                { error = $"DNS secondario non valido: '{Dns2}'"; return false; }
            error = "";
            return true;
        }

        private async void ApplyConfig()
        {
            if (_selected == null) return;

            if (!DhcpMode && !ValidateStatic(out string err))
            {
                AddLog("WARN", err);
                MessageBox.Show(err, "Validazione", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Busy = true;
            AddLog("INFO", $"Applicazione configurazione su {_selected.Name}...");

            var adapter = _selected;
            var (ok, output) = await Task.Run(() =>
                NetworkService.ApplyConfig(adapter, DhcpMode, Ip, Subnet, Gateway, Dns1, Dns2));

            if (ok)
            {
                AddLog("OK", output.Length > 0 ? output : "Configurazione applicata.");
                MessageBox.Show(
                    $"✅ {(output.Length > 0 ? output : "Configurazione applicata con successo.")}",
                    "Successo", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadAdapters();
            }
            else
            {
                AddLog("WARN", output);
                MessageBox.Show($"❌ {output}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            Busy = false;
        }

        private void AddLog(string type, string msg)
        {
            var ts  = DateTime.Now.ToString("HH:mm:ss");
            var pfx = type switch { "OK" => "[ OK ]", "WARN" => "[WARN]", _ => "[INFO]" };
            Log += $"{ts}  {pfx}  {msg}\n";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
