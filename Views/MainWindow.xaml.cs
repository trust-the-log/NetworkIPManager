using System.Windows;
using System.Windows.Controls;
using NetworkIPManager.ViewModels;

namespace NetworkIPManager.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void DhcpRadio_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.SetDhcpMode();
        }

        private void StaticRadio_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.SetStaticMode();
        }
    }
}
