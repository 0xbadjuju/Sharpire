using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Sharpire
{
    public class Misc
    {
        //https://stackoverflow.com/questions/3571627/show-hide-the-console-window-of-a-c-sharp-console-application
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, Int32 nCmdShow);

        public const Int32 SW_HIDE = 0;
        public const Int32 SW_SHOW = 5;

        ////////////////////////////////////////////////////////////////////////////////
        public static byte[] combine(byte[] byte1, byte[] byte2)
            {
                Int32 dwSize = byte1.Length + byte2.Length;
                MemoryStream memoryStream = new MemoryStream(new byte[dwSize], 0, dwSize, true, true);
                memoryStream.Write(byte1, 0, byte1.Length);
                memoryStream.Write(byte2, 0, byte2.Length);
                byte[] combinedBytes = memoryStream.GetBuffer();
                return combinedBytes;
            }
        }
}