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
    public partial class GetPlaneModal : Window
    {
        public GetPlaneModal()
        {
            InitializeComponent();
        }

        public Windows.Quad quad = null;

        private void OnYesClick(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(X1.Text, out double x1))
            {
                return;
            }
            if (!double.TryParse(X2.Text, out double x2))
            {
                return;
            }
            if (!double.TryParse(X3.Text, out double x3))
            {
                return;
            }
            if (!double.TryParse(X4.Text, out double x4))
            {
                return;
            }

            if (!double.TryParse(Y1.Text, out double y1))
            {
                return;
            }
            if (!double.TryParse(Y2.Text, out double y2))
            {
                return;
            }
            if (!double.TryParse(Y3.Text, out double y3))
            {
                return;
            }
            if (!double.TryParse(Y4.Text, out double y4))
            {
                return;
            }

            if (!double.TryParse(Z1.Text, out double z1))
            {
                return;
            }
            if (!double.TryParse(Z2.Text, out double z2))
            {
                return;
            }
            if (!double.TryParse(Z3.Text, out double z3))
            {
                return;
            }
            if (!double.TryParse(Z4.Text, out double z4))
            {
                return;
            }

            quad = new Windows.Quad(new Windows.Vector3(x1, y1, z1), new Windows.Vector3(x2, y2, z2), new Windows.Vector3(x3, y3, z3), new Windows.Vector3(x4, y4, z4));

            DialogResult = true;
        }

        private void OnNoClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
