using System;
using System.Collections;
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

namespace VolumeStates.Windows
{
    public class Quad
    {
        public Vector3 S1;
        public Vector3 S2;
        public Vector3 S3;
        public Vector3 S4;

        public Quad(Vector3 s1, Vector3 s2, Vector3 s3, Vector3 s4)
        {
            S1 = s1;
            S2 = s2;
            S3 = s3;
            S4 = s4;
        }
    }

    public class Line
    {
        public Vector3 Start;
        public Vector3 End;

        public Line(Vector3 start, Vector3 end)
        {
            Start = start;
            End = end;
        }
    }

    public class Vector3
    {
        public double x, y, z;

        public Vector3(double x, double y, double z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public Vector3 add(Vector3 other)
        {
            return new Vector3(x + other.x, y + other.y, z + other.z);
        }

        public Vector3 sub(Vector3 other)
        {
            return new Vector3(x - other.x, y - other.y, z - other.z);
        }

        public Vector3 scale(double f)
        {
            return new Vector3(x * f, y * f, z * f);
        }

        public Vector3 cross(Vector3 other)
        {
            return new Vector3(y * other.z - z * other.y,
                               z - other.x - x * other.z,
                               x - other.y - y * other.x);
        }

        public double dot(Vector3 other)
        {
            return x * other.x + y * other.y + z * other.z;
        }

        public static bool intersectRayWithSquare(Line line, Quad square)
        {
            // 1.
            Vector3 dS21 = square.S2.sub(square.S1);
            Vector3 dS31 = square.S3.sub(square.S1);
            Vector3 n = dS21.cross(dS31);

            // 2.
            Vector3 dR = line.Start.sub(line.End);

            double ndotdR = n.dot(dR);

            if (Math.Abs(ndotdR) < 1e-6f)
            { // Choose your tolerance
                return false;
            }

            double t = -n.dot(line.Start.sub(square.S1)) / ndotdR;
            Vector3 M = line.Start.add(dR.scale(t));

            // 3.
            Vector3 dMS1 = M.sub(square.S1);
            double u = dMS1.dot(dS21);
            double v = dMS1.dot(dS31);

            // 4.
            return (u >= 0.0f && u <= dS21.dot(dS21)
                 && v >= 0.0f && v <= dS31.dot(dS31));
        }
    }

    public partial class PositionTracking : Window
    {
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            RenderPosition();
        }

        public void RenderPosition()
        {
            if (X == null || !double.TryParse(X.Text, out double x) || MinX == null || !double.TryParse(MinX.Text, out double minX) || MaxX == null || !double.TryParse(MaxX.Text, out double maxX))
            {
                return;
            }
            if (Z == null || !double.TryParse(Z.Text, out double z) || MinZ == null || !double.TryParse(MinZ.Text, out double minZ) || MaxZ == null || !double.TryParse(MaxZ.Text, out double maxZ))
            {
                return;
            }

            Vector bounds = new Vector(BG.Width, BG.Height);
            Vector relativePlayerPos = new Vector((double)(x - minX) / (double)(maxX - minX), (double)(z - minZ) / (double)(maxZ - minZ));
            Vector playerPosInBG = new Vector(relativePlayerPos.X * bounds.X, relativePlayerPos.Y * bounds.Y);

            if (double.IsInfinity(playerPosInBG.X) || double.IsNaN(playerPosInBG.X))
            {
                playerPosInBG.X = 0;
            }

            if (double.IsInfinity(playerPosInBG.Y) || double.IsNaN(playerPosInBG.Y))
            {
                playerPosInBG.Y = 0;
            }

            PlayerPos.Margin = new Thickness(5 + playerPosInBG.X, 5 + playerPosInBG.Y, 0, 0);
        }

        public void SetPlayerPosition(float x, float y, float z)
        {
            X.Dispatcher.BeginInvoke((Action)(() => { X.Text = x.ToString(); }));
            Y.Dispatcher.BeginInvoke((Action)(() => { Y.Text = y.ToString(); }));
            Z.Dispatcher.BeginInvoke((Action)(() => { Z.Text = z.ToString(); }));

            PosTrackWindow.Dispatcher.BeginInvoke((Action)(() => RenderPosition()));
            BelowThreshold.Dispatcher.BeginInvoke((Action)(() => { BelowThreshold.IsChecked = y < 22.8f; }));
        }
        PositionWatcher watcher;

        public PositionTracking()
        {
            InitializeComponent();

            watcher = new PositionWatcher();
            watcher.StartWatcher((byte[] newPositionMemory) =>
            {
                SetPlayerPosition(BitConverter.ToSingle(newPositionMemory, 0), BitConverter.ToSingle(newPositionMemory, 4), BitConverter.ToSingle(newPositionMemory, 8));
            });
        }

        private void MinX_TextChanged(object sender, TextChangedEventArgs e)
        {
            RenderPosition();
        }

        private void MinZ_TextChanged(object sender, TextChangedEventArgs e)
        {
            RenderPosition();
        }

        private void MaxX_TextChanged(object sender, TextChangedEventArgs e)
        {
            RenderPosition();
        }

        private void MaxZ_TextChanged(object sender, TextChangedEventArgs e)
        {
            RenderPosition();
        }

        List<Quad> gates = new List<Quad>();

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            GetPlaneModal buttonListenModal = new GetPlaneModal();
            buttonListenModal.Owner = this;
            if (buttonListenModal.ShowDialog() == true)
            {
                gates.Add(buttonListenModal.quad);
            }
        }
    }
}
