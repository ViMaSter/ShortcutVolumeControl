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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace volumeStates
{
    public partial class MainWindow : Window
    {
        AudioDevice currentlyActiveDevice;

        public MainWindow()
        {
            InitializeComponent();

            currentlyActiveDevice = AudioUtilities.GetDefaultSpeaker();

            foreach (AudioSession session in AudioUtilities.GetAllSessions())
            {
                if (session.Process != null)
                {
                    // only the one associated with a defined process
                    Console.WriteLine(session.Process.ProcessName + ": " + session.Volume);
                }
            }
        }
    }
}
