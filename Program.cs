using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using Microsoft.Win32.SafeHandles;

using MindTouch.Deki.Import;
using MindTouch.Tasking;
using MindTouch.Xml;

namespace chmdump {
    class Program {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateDirectoryW(
            string lpPathName,
            IntPtr lpSecurityAttributes);

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Unicode,
            CallingConvention = CallingConvention.StdCall,
            SetLastError = true)]
        public static extern SafeFileHandle CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr SecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(SafeFileHandle hObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool WriteFile(
            SafeFileHandle hFile, 
            byte[] lpBuffer, 
            uint nNumberOfBytesToWrite, 
            out uint lpNumberOfBytesWritten,
            [In] ref System.Threading.NativeOverlapped lpOverlapped);

        const uint GENERIC_READ = 0x80000000;
        const uint GENERIC_WRITE = 0x40000000;

        const int CREATE_ALWAYS = 2;

        const int FILE_ATTRIBUTE_NORMAL = 0x80;

        static void Main(string[] args) {
            if(args.Length != 2) {
                Console.WriteLine("Dump a CHM file to disk using the MindTouch import library\n");
                Console.WriteLine("USAGE: chmdump.exe <CHM file> <output directory>");
                return;
            }

            if (!Directory.Exists(args[1])) {
                Directory.CreateDirectory(args[1]);
            }

            Console.WriteLine("Reading file {0}...", args[0]);
            ChmPackageReader cpr = new ChmPackageReader(args[0], null, "");

            Console.WriteLine("Generating manifest...");
            XDoc manifest = cpr.ReadManifest(new Result<XDoc>()).Value;
            File.WriteAllText(args[1] + "/manifest.xml", manifest.ToPrettyString());

            Console.WriteLine("Dumping archive contents to {0}...", args[1]);
            foreach(XDoc item in manifest["//manifest/*[@dataid != '']"]) {
                ImportItem ii = new ImportItem(item["@dataid"].AsText, null, manifest);
                ii = cpr.ReadData(ii, new Result<ImportItem>()).Value;

                string path = (args[1] + "/" + item["path"].AsText.TrimStart('/'))
                    .Replace('/', '\\')
                    .TrimEnd('\\');
                string filename = null;
                CreateDirectoryW(@"\\?\" + path, IntPtr.Zero);

                switch(item.Name) {
                case "page":
                    filename = "page.html";

                    byte[] titlebuf = ASCIIEncoding.UTF8.GetBytes(item["title"].Contents);
                    uint written = 0;
                    NativeOverlapped no = new NativeOverlapped();
                    SafeFileHandle titlesfh = CreateFileW(
                        @"\\?\" + path + @"\title.txt",
                        GENERIC_READ | GENERIC_WRITE,
                        0,
                        IntPtr.Zero,
                        CREATE_ALWAYS,
                        FILE_ATTRIBUTE_NORMAL,
                        IntPtr.Zero);
                    WriteFile(
                        titlesfh,
                        titlebuf,
                        (uint)titlebuf.Length,
                        out written,
                        ref no);
                    CloseHandle(titlesfh);
                    break;

                case "file":
                    filename = item["filename"].Contents;
                    break;

                case "tags":
                    filename = "tags.xml";
                    break;
                }

                SafeFileHandle sfh = CreateFileW(
                    @"\\?\" + path + @"\" + filename,
                    GENERIC_READ | GENERIC_WRITE,
                    0,
                    IntPtr.Zero,
                    CREATE_ALWAYS,
                    FILE_ATTRIBUTE_NORMAL,
                    IntPtr.Zero);
                if(sfh.IsInvalid) {
                    Console.WriteLine("  " + Marshal.GetLastWin32Error() + " " + path + @"\" + filename);
                    CloseHandle(sfh);
                    continue;
                }

                byte[] buf = new byte[10240];
                int len = 0;

                while((len = ii.Data.Read(buf, 0, 10240)) > 0) {
                    uint written = 0;
                    NativeOverlapped no = new NativeOverlapped();
                    WriteFile(sfh, buf, (uint)len, out written, ref no);
                }

                CloseHandle(sfh);
            }
        }
    }
}
