using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

#if WINDOWS
using DirectShowLib;
#endif

namespace Baballonia.VFTCapture.Windows;

/// <summary>
/// Maps a DirectShow camera index to the Media Foundation index of the same physical device.
/// The device enumerator numbers cameras in DirectShow order, but OpenCV's MSMF backend numbers them
/// in Media Foundation order, and the two orders differ when other cameras (for example virtual webcams) are installed.
/// </summary>
public static class CameraIndexMapper
{
    /// <summary>
    /// Returns the Media Foundation index of the camera at <paramref name="directShowIndex"/>,
    /// or null when the device cannot be matched.
    /// </summary>
    public static int? DirectShowToMediaFoundation(int directShowIndex, ILogger logger)
    {
        var directShowPath = GetDirectShowDevicePath(directShowIndex);
        if (directShowPath is null)
        {
            logger.LogWarning("VFT: no DirectShow video device at index {Index}", directShowIndex);
            return null;
        }

        var mediaFoundationPaths = MediaFoundationDevices.GetVideoCaptureSymbolicLinks();
        var index = FindMatchingIndex(directShowPath, mediaFoundationPaths);
        if (index is null)
        {
            logger.LogWarning("VFT: DirectShow device {Path} has no Media Foundation counterpart among {Count} devices",
                directShowPath, mediaFoundationPaths.Count);
        }

        return index;
    }

    /// <summary>
    /// Returns the index in <paramref name="candidatePaths"/> of the device with the same instance as <paramref name="devicePath"/>.
    /// </summary>
    public static int? FindMatchingIndex(string devicePath, IReadOnlyList<string> candidatePaths)
    {
        var key = GetDeviceInstanceKey(devicePath);
        if (key is null)
            return null;

        for (var i = 0; i < candidatePaths.Count; i++)
        {
            if (string.Equals(GetDeviceInstanceKey(candidatePaths[i]), key, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return null;
    }

    /// <summary>
    /// Extracts the device instance part of a device interface path, which is shared by every interface of one device.
    /// For "@device:pnp:\\?\usb#vid_0bb4&amp;pid_0321&amp;mi_00#e&amp;ccef36c&amp;0&amp;0000#{65e8773d-...}\global"
    /// this returns "usb#vid_0bb4&amp;pid_0321&amp;mi_00#e&amp;ccef36c&amp;0&amp;0000".
    /// </summary>
    public static string? GetDeviceInstanceKey(string devicePath)
    {
        const string prefix = @"\\?\";
        var start = devicePath.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
            return null;

        start += prefix.Length;
        var end = devicePath.IndexOf("#{", start, StringComparison.Ordinal);
        if (end <= start)
            return null;

        return devicePath[start..end].ToLowerInvariant();
    }

    private static string? GetDirectShowDevicePath(int index)
    {
#if WINDOWS
        var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
        try
        {
            return index >= 0 && index < devices.Length ? devices[index].DevicePath : null;
        }
        finally
        {
            foreach (var device in devices)
                device.Dispose();
        }
#else
        return null;
#endif
    }
}

internal static class MediaFoundationDevices
{
    private static readonly Guid SourceTypeAttribute = new("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3");
    private static readonly Guid SourceTypeVideoCapture = new("8ac3587a-4ae7-42d8-99e0-0a6013eef90f");
    private static readonly Guid VideoCaptureSymbolicLinkAttribute = new("58f0aad8-22bf-4f8a-bb3d-d2c4978c6e2f");
    private const int MfVersion = 0x00020070;

    /// <summary>
    /// Lists video capture device symbolic links in MFEnumDeviceSources order, which is the order OpenCV's MSMF backend indexes.
    /// </summary>
    public static List<string> GetVideoCaptureSymbolicLinks()
    {
        var links = new List<string>();
        if (MFStartup(MfVersion, 0) < 0)
            return links;

        try
        {
            if (MFCreateAttributes(out var attributes, 1) < 0)
                return links;

            try
            {
                if (attributes.SetGUID(SourceTypeAttribute, SourceTypeVideoCapture) < 0 ||
                    MFEnumDeviceSources(attributes, out var devices, out var count) < 0)
                    return links;

                for (var i = 0; i < count; i++)
                {
                    var unknown = Marshal.ReadIntPtr(devices, i * IntPtr.Size);
                    var device = (IMFAttributes)Marshal.GetObjectForIUnknown(unknown);
                    if (device.GetAllocatedString(VideoCaptureSymbolicLinkAttribute, out var value, out _) >= 0)
                    {
                        links.Add(Marshal.PtrToStringUni(value) ?? string.Empty);
                        Marshal.FreeCoTaskMem(value);
                    }
                    else
                    {
                        links.Add(string.Empty);
                    }

                    Marshal.ReleaseComObject(device);
                    Marshal.Release(unknown);
                }

                Marshal.FreeCoTaskMem(devices);
            }
            finally
            {
                Marshal.ReleaseComObject(attributes);
            }
        }
        finally
        {
            MFShutdown();
        }

        return links;
    }

    [DllImport("mfplat.dll")]
    private static extern int MFStartup(int version, int flags);

    [DllImport("mfplat.dll")]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll")]
    private static extern int MFCreateAttributes(out IMFAttributes attributes, int initialSize);

    [DllImport("mf.dll")]
    private static extern int MFEnumDeviceSources(IMFAttributes attributes, out IntPtr activates, out int count);

    // Only the members used here are typed; the others keep their vtable slots.
    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFAttributes
    {
        void GetItem();
        void GetItemType();
        void CompareItem();
        void Compare();
        void GetUINT32();
        void GetUINT64();
        void GetDouble();
        void GetGUID();
        void GetStringLength();
        void GetString();
        [PreserveSig] int GetAllocatedString(in Guid key, out IntPtr value, out int length);
        void GetBlobSize();
        void GetBlob();
        void GetAllocatedBlob();
        void GetUnknown();
        void SetItem();
        void DeleteItem();
        void DeleteAllItems();
        void SetUINT32();
        void SetUINT64();
        void SetDouble();
        [PreserveSig] int SetGUID(in Guid key, in Guid value);
    }
}
