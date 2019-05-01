using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace VolumeStates
{
    /// <summary>
    /// Interaction logic for ConnectToFFXIV.xaml
    /// </summary>
    public partial class ConnectToFFXIVWindow : Window
    {
        public ConnectToFFXIVWindow()
        {
            InitializeComponent();
        }

        private void OnYesClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void OnNoClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
