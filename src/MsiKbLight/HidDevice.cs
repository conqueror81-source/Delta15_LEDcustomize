using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MsiKbLight;

/// <summary>Minimal HID access via the inbox hid.dll / setupapi.dll (no driver required).</summary>
internal sealed class HidDevice : IDisposable
{
    private readonly SafeFileHandle _handle;

    public string Path { get; }
    public ushort VendorId { get; }
    public ushort ProductId { get; }
    public int FeatureReportLength { get; }

    private HidDevice(string path, SafeFileHandle handle, ushort vid, ushort pid, int featureLen)
    {
        Path = path;
        _handle = handle;
        VendorId = vid;
        ProductId = pid;
        FeatureReportLength = featureLen;
    }

    /// <summary>Opens the first present HID interface that matches the VID and any of the PIDs.</summary>
    public static HidDevice? Open(ushort vid, IReadOnlyCollection<ushort> pids)
    {
        foreach (var path in EnumerateInterfacePaths())
        {
            var handle = Native.CreateFile(path, Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                continue;
            }

            var attr = new Native.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<Native.HIDD_ATTRIBUTES>() };
            if (!Native.HidD_GetAttributes(handle, ref attr) || attr.VendorID != vid || !pids.Contains(attr.ProductID))
            {
                handle.Dispose();
                continue;
            }

            int featureLen = 0;
            if (Native.HidD_GetPreparsedData(handle, out var preparsed))
            {
                if (Native.HidP_GetCaps(preparsed, out var caps) == Native.HIDP_STATUS_SUCCESS)
                    featureLen = caps.FeatureReportByteLength;
                Native.HidD_FreePreparsedData(preparsed);
            }

            return new HidDevice(path, handle, attr.VendorID, attr.ProductID, featureLen);
        }
        return null;
    }

    public void SetFeature(byte[] report)
    {
        var buf = Fit(report);
        if (!Native.HidD_SetFeature(_handle, buf, buf.Length))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"HidD_SetFeature (report 0x{report[0]:X2}) failed");
    }

    public byte[] GetFeature(byte reportId)
    {
        var buf = new byte[FeatureReportLength > 0 ? FeatureReportLength : 64];
        buf[0] = reportId;
        if (!Native.HidD_GetFeature(_handle, buf, buf.Length))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"HidD_GetFeature (report 0x{reportId:X2}) failed");
        return buf;
    }

    // The buffer passed to HidD_*Feature must be exactly the collection's feature report length.
    private byte[] Fit(byte[] report)
    {
        if (FeatureReportLength <= 0 || report.Length == FeatureReportLength)
            return report;
        var buf = new byte[FeatureReportLength];
        Array.Copy(report, buf, Math.Min(report.Length, buf.Length));
        return buf;
    }

    public void Dispose() => _handle.Dispose();

    private static IEnumerable<string> EnumerateInterfacePaths()
    {
        Native.HidD_GetHidGuid(out var hidGuid);
        var set = Native.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1))
            yield break;

        try
        {
            var ifData = new Native.SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<Native.SP_DEVICE_INTERFACE_DATA>() };
            for (uint i = 0; Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref ifData); i++)
            {
                Native.SetupDiGetDeviceInterfaceDetail(set, ref ifData, IntPtr.Zero, 0, out int required, IntPtr.Zero);
                if (required <= 0)
                    continue;

                var detail = Marshal.AllocHGlobal(required);
                try
                {
                    // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 on x64, 6 on x86.
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref ifData, detail, required, out _, IntPtr.Zero))
                        continue;
                    var path = Marshal.PtrToStringUni(detail + 4);
                    if (!string.IsNullOrEmpty(path))
                        yield return path;
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            Native.SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static class Native
    {
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x1;
        public const uint FILE_SHARE_WRITE = 0x2;
        public const uint OPEN_EXISTING = 3;
        public const uint DIGCF_PRESENT = 0x2;
        public const uint DIGCF_DEVICEINTERFACE = 0x10;
        public const int HIDP_STATUS_SUCCESS = 0x00110000;

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [DllImport("hid.dll")]
        public static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetAttributes(SafeFileHandle device, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_SetFeature(SafeFileHandle device, byte[] buffer, int length);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetFeature(SafeFileHandle device, byte[] buffer, int length);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

        [DllImport("hid.dll")]
        public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll")]
        public static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData,
            ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData,
            int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
            IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
    }
}
