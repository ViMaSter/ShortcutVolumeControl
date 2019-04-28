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

namespace volumeStates
{
    /// <summary>
    /// Interaction logic for ButtonListenModal.xaml
    /// </summary>
    public partial class ButtonListenModal : Window
    {
        private ModifierKeys modifiers;
        public ModifierKeys Modifiers
        {
            get { return modifiers; }
        }
        private Key pressedKey;
        public Key PressedKey
        {
            get { return pressedKey; }
        }
        public ButtonListenModal()
        {
            InitializeComponent();
        }

        private void OnClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            modifiers = Keyboard.Modifiers;
            pressedKey = e.Key;
            DialogResult = true;
        }
    }
}
