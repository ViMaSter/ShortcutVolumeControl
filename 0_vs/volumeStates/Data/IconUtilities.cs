using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class IconUtilities
{
    static class NativeMethods
    {
        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr hObject);
    }

    public static ImageSource ToImageSource(this Icon icon)
    {
        Bitmap bitmap = icon.ToBitmap();
        IntPtr hBitmap = bitmap.GetHbitmap();

        ImageSource wpfBitmap = Imaging.CreateBitmapSourceFromHBitmap(
            hBitmap,
            IntPtr.Zero,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());

        if (!NativeMethods.DeleteObject(hBitmap))
        {
            throw new Win32Exception();
        }

        return wpfBitmap;
    }
}

