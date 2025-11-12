using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace DropZoneApp
{
    public static class OutlookDragDrop
    {
        private const string CFSTR_FILEDESCRIPTORW = "FileGroupDescriptorW";
        private const string CFSTR_FILEDESCRIPTORA = "FileGroupDescriptor";
        private const string CFSTR_FILECONTENTS    = "FileContents";

        [Flags]
        private enum FD : uint
        {
            FD_ATTRIBUTES = 0x00000004,
            FD_FILESIZE   = 0x00000040,
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
        private struct FILEDESCRIPTORW
        {
            public uint dwFlags;
            public Guid clsid;
            public System.Drawing.Size sizel;
            public System.Drawing.Point pointl;
            public uint dwFileAttributes;
            public ComTypes.FILETIME ftCreationTime;
            public ComTypes.FILETIME ftLastAccessTime;
            public ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 4)]
        private struct FILEDESCRIPTORA
        {
            public uint dwFlags;
            public Guid clsid;
            public System.Drawing.Size sizel;
            public System.Drawing.Point pointl;
            public uint dwFileAttributes;
            public ComTypes.FILETIME ftCreationTime;
            public ComTypes.FILETIME ftLastAccessTime;
            public ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("0000000B-0000-0000-C000-000000000046")]
        private interface IStorage
        {
            void CreateStream([MarshalAs(UnmanagedType.LPWStr)] string pwcsName, uint grfMode, uint reserved1, uint reserved2, out IntPtr ppstm);
            void OpenStream([MarshalAs(UnmanagedType.LPWStr)] string pwcsName, IntPtr reserved1, uint grfMode, uint reserved2, out IntPtr ppstm);
            void CreateStorage([MarshalAs(UnmanagedType.LPWStr)] string pwcsName, uint grfMode, uint reserved1, uint reserved2, out IStorage ppstg);
            void OpenStorage([MarshalAs(UnmanagedType.LPWStr)] string pwcsName, IStorage pstgPriority, uint grfMode, IntPtr snbExclude, uint reserved, out IStorage ppstg);
            void CopyTo(uint ciidExclude, IntPtr rgiidExclude, IntPtr snbExclude, IStorage pstgDest);
            void MoveElementTo([MarshalAs(UnmanagedType.LPWStr)] string pwcsName, IStorage pstgDest, [MarshalAs(UnmanagedType.LPWStr)] string pwcsNewName, uint grfFlags);
            void Commit(uint grfCommitFlags);
            void Revert();
            void EnumElements(uint reserved1, IntPtr reserved2, uint reserved3, out IntPtr ppenum);
            void DestroyElement([MarshalAs(UnmanagedType.LPWStr)] string pwcsName);
            void RenameElement([MarshalAs(UnmanagedType.LPWStr)] string pwcsOldName, [MarshalAs(UnmanagedType.LPWStr)] string pwcsNewName);
            void SetElementTimes([MarshalAs(UnmanagedType.LPWStr)] string pwcsName, ref ComTypes.FILETIME pctime, ref ComTypes.FILETIME patime, ref ComTypes.FILETIME pmtime);
            void SetClass(ref Guid clsid);
            void SetStateBits(uint grfStateBits, uint grfMask);
            void Stat(out ComTypes.STATSTG pstatstg, uint grfStatFlag);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern ushort RegisterClipboardFormat(string lpszFormat);
        [DllImport("ole32.dll")]
        private static extern void ReleaseStgMedium(ref ComTypes.STGMEDIUM pmedium);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr hMem);
        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr hMem);
        [DllImport("kernel32.dll")]
        private static extern int GlobalSize(IntPtr hMem);
        [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
        private static extern int StgCreateDocfile(string pwcsName, uint grfMode, uint reserved, out IStorage ppstgOpen);

        private const uint STGM_READWRITE       = 0x00000002;
        private const uint STGM_SHARE_EXCLUSIVE = 0x00000010;
        private const uint STGM_CREATE          = 0x00001000;

        public static bool TryGetOutlookFiles(IDataObject dataObj, out List<(string FileName, Stream Content, long? Length)> items)
        {
            items = new List<(string, Stream, long?)>();
            var names = GetFilenames(dataObj, out List<(uint? sizeHigh, uint? sizeLow)> sizes);
            if (names.Count == 0) return false;

            for (int i = 0; i < names.Count; i++)
            {
                Stream? stream = TryGetContentStream(dataObj, i, out long? length);
                if (stream == null && i == 0)
                {
                    var single = dataObj.GetData(CFSTR_FILECONTENTS, true);
                    if (single is MemoryStream ms) { length ??= TryLength(ms); stream = new MemoryStream(ms.ToArray()); }
                }

                if (stream != null)
                {
                    long? lenFromDesc = CombineLength(sizes[i]);
                    if (lenFromDesc.HasValue) length = lenFromDesc;
                    string name = SanitizeFileName(names[i]);
                    if (!name.EndsWith(".msg", StringComparison.OrdinalIgnoreCase) && LooksLikeMailDescriptor(names[i]))
                        name += ".msg";

                    items.Add((name, stream, length));
                }
            }
            return items.Count > 0;
        }

        private static Stream? TryGetContentStream(IDataObject dataObj, int index, out long? length)
        {
            length = null;
            try
            {
                if (dataObj is ComTypes.IDataObject comObj)
                {
                    var fmt = new ComTypes.FORMATETC
                    {
                        cfFormat = (short)RegisterClipboardFormat(CFSTR_FILECONTENTS),
                        ptd = IntPtr.Zero,
                        dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT,
                        lindex = index,
                        tymed = ComTypes.TYMED.TYMED_ISTREAM | ComTypes.TYMED.TYMED_HGLOBAL | ComTypes.TYMED.TYMED_ISTORAGE
                    };

                    comObj.GetData(ref fmt, out ComTypes.STGMEDIUM med);
                    try
                    {
                        if (med.tymed == ComTypes.TYMED.ISTREAM || med.tymed == ComTypes.TYMED.TYMED_ISTREAM)
                        {
                            var unk = med.unionmember;
                            if (unk != IntPtr.Zero)
                            {
                                var iStream = (ComTypes.IStream)Marshal.GetObjectForIUnknown(unk);
                                var ms = new MemoryStream();
                                CopyToManaged(iStream, ms, out long bytes);
                                ms.Position = 0;
                                length = bytes;
                                return ms;
                            }
                        }
                        else if (med.tymed == ComTypes.TYMED.TYMED_HGLOBAL)
                        {
                            var h = med.unionmember;
                            if (h != IntPtr.Zero)
                            {
                                var ptr = GlobalLock(h);
                                try
                                {
                                    int size = GlobalSize(h);
                                    byte[] buffer = new byte[size];
                                    Marshal.Copy(ptr, buffer, 0, size);
                                    length = size;
                                    return new MemoryStream(buffer, writable: false);
                                }
                                finally { GlobalUnlock(h); }
                            }
                        }
                        else if (med.tymed == ComTypes.TYMED.TYMED_ISTORAGE)
                        {
                            var unk = med.unionmember;
                            if (unk != IntPtr.Zero)
                            {
                                var storage = (IStorage)Marshal.GetObjectForIUnknown(unk);
                                var ms = IStorageToMsgStream_File(storage);
                                length = ms.Length;
                                ms.Position = 0;
                                return ms;
                            }
                        }
                    }
                    finally
                    {
                        try { ReleaseStgMedium(ref med); } catch { }
                    }
                }
            }
            catch { }

            try
            {
                object? obj = dataObj.GetData(CFSTR_FILECONTENTS, true);
                if (obj is MemoryStream ms)
                {
                    length = TryLength(ms);
                    return new MemoryStream(ms.ToArray());
                }
                if (obj is MemoryStream[] arr && index < arr.Length)
                {
                    var msi = arr[index];
                    length = TryLength(msi);
                    return new MemoryStream(msi.ToArray());
                }
            }
            catch { }
            return null;
        }

        private static MemoryStream IStorageToMsgStream_File(IStorage src)
        {
            string temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".msg");
            IStorage dst;
            int hr = StgCreateDocfile(temp, STGM_CREATE | STGM_READWRITE | STGM_SHARE_EXCLUSIVE, 0, out dst);
            if (hr != 0) throw new InvalidOperationException("StgCreateDocfile failed, HRESULT=0x" + hr.ToString("X"));

            try
            {
                src.CopyTo(0, IntPtr.Zero, IntPtr.Zero, dst);
                dst.Commit(0);
            }
            finally
            {
                try { Marshal.FinalReleaseComObject(dst); } catch { }
            }

            try { return new MemoryStream(File.ReadAllBytes(temp), writable: false); }
            finally { try { File.Delete(temp); } catch { } }
        }

        private static List<string> GetFilenames(IDataObject dataObj, out List<(uint?, uint?)> sizes)
        {
            sizes = new();
            var list = new List<string>();

            if (TryReadFileGroupDescriptor(dataObj, CFSTR_FILEDESCRIPTORW, isUnicode: true, out var namesW, out var sizesW))
            { sizes = sizesW; return namesW; }
            if (TryReadFileGroupDescriptor(dataObj, CFSTR_FILEDESCRIPTORA, isUnicode: false, out var namesA, out var sizesA))
            { sizes = sizesA; return namesA; }
            return list;
        }

        private static bool TryReadFileGroupDescriptor(IDataObject dataObj, string format, bool isUnicode,
                                                       out List<string> names, out List<(uint?, uint?)> sizes)
        {
            names = new(); sizes = new();

            try
            {
                if (dataObj.GetDataPresent(format))
                {
                    if (dataObj.GetData(format) is MemoryStream ms)
                    {
                        ReadDescriptorsFromStream(ms.ToArray(), isUnicode, out names, out sizes);
                        if (names.Count > 0) return true;
                    }
                }
            }
            catch { }

            try
            {
                if (dataObj is ComTypes.IDataObject comObj)
                {
                    var fmt = new ComTypes.FORMATETC
                    {
                        cfFormat = (short)RegisterClipboardFormat(format),
                        ptd = IntPtr.Zero,
                        dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT,
                        lindex = -1,
                        tymed = ComTypes.TYMED.TYMED_HGLOBAL
                    };
                    comObj.GetData(ref fmt, out ComTypes.STGMEDIUM med);
                    try
                    {
                        if (med.tymed == ComTypes.TYMED.TYMED_HGLOBAL)
                        {
                            var h = med.unionmember;
                            if (h != IntPtr.Zero)
                            {
                                var ptr = GlobalLock(h);
                                try
                                {
                                    int size = GlobalSize(h);
                                    byte[] buffer = new byte[size];
                                    Marshal.Copy(ptr, buffer, 0, size);
                                    ReadDescriptorsFromStream(buffer, isUnicode, out names, out sizes);
                                    return names.Count > 0;
                                }
                                finally { GlobalUnlock(h); }
                            }
                        }
                    }
                    finally { try { ReleaseStgMedium(ref med); } catch { } }
                }
            }
            catch { }

            return false;
        }

        private static void ReadDescriptorsFromStream(byte[] buffer, bool isUnicode,
                                                      out List<string> names, out List<(uint?, uint?)> sizes)
        {
            names = new(); sizes = new();
            using var ms = new MemoryStream(buffer);
            using var br = new BinaryReader(ms, isUnicode ? Encoding.Unicode : Encoding.Default);
            int count = br.ReadInt32();
            if (count <= 0) return;

            if (isUnicode)
            {
                int cb = Marshal.SizeOf<FILEDESCRIPTORW>();
                int offset = 4;
                for (int i = 0; i < count; i++)
                {
                    var fd = BytesToStructure<FILEDESCRIPTORW>(buffer, offset);
                    names.Add(SanitizeFileName(fd.cFileName));
                    sizes.Add(((fd.dwFlags & (uint)FD.FD_FILESIZE) != 0) ? (fd.nFileSizeHigh, fd.nFileSizeLow) : (null, null));
                    offset += cb;
                }
            }
            else
            {
                int cb = Marshal.SizeOf<FILEDESCRIPTORA>();
                int offset = 4;
                for (int i = 0; i < count; i++)
                {
                    var fd = BytesToStructure<FILEDESCRIPTORA>(buffer, offset);
                    names.Add(SanitizeFileName(fd.cFileName));
                    sizes.Add(((fd.dwFlags & (uint)FD.FD_FILESIZE) != 0) ? (fd.nFileSizeHigh, fd.nFileSizeLow) : (null, null));
                    offset += cb;
                }
            }
        }

        private static T BytesToStructure<T>(byte[] buffer, int offset) where T : struct
        {
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                IntPtr ptr = IntPtr.Add(handle.AddrOfPinnedObject(), offset);
                return Marshal.PtrToStructure<T>(ptr);
            }
            finally { handle.Free(); }
        }

        private static void CopyToManaged(ComTypes.IStream src, Stream dst, out long bytes)
        {
            bytes = 0;
            var buffer = new byte[1024 * 1024];
            var readPtr = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                while (true)
                {
                    src.Read(buffer, buffer.Length, readPtr);
                    int read = Marshal.ReadInt32(readPtr);
                    if (read <= 0) break;
                    dst.Write(buffer, 0, read);
                    bytes += read;
                }
                dst.Flush();
            }
            finally
            {
                Marshal.FreeHGlobal(readPtr);
            }
        }

        private static long? CombineLength((uint? hi, uint? lo) t)
        {
            if (!t.hi.HasValue || !t.lo.HasValue) return null;
            return ((long)t.hi.Value << 32) + t.lo.Value;
        }

        private static long? TryLength(Stream s) => s.CanSeek ? s.Length : (long?)null;

        private static string SanitizeFileName(string name)
        {
            var s = name.Replace(' ', '_');
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim().TrimEnd('.');
        }

        private static bool LooksLikeMailDescriptor(string name)
        {
            if (name.EndsWith(".msg", StringComparison.OrdinalIgnoreCase)) return true;
            return !System.IO.Path.HasExtension(name);
        }
    }
}
