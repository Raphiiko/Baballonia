using Baballonia.VFTCapture.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Baballonia.Tests;

[TestClass]
public class CameraIndexMapperTest
{
    private const string DirectShowVftPath =
        @"@device:pnp:\\?\usb#vid_0bb4&pid_0321&mi_00#e&ccef36c&0&0000#{65e8773d-8f56-11d0-a3b9-00a0c9223196}\global";

    // Media Foundation order seen with a virtual webcam installed: the webcam sorts before the tracker.
    private static readonly string[] MediaFoundationPaths =
    [
        @"\\?\root#devgen#{03aeb1a2-2746-1846-b2bb-ddf18b997404}#{e5323777-f976-4f5b-9b55-b94699c46e44}\8da4e6f4fd61466dbca9c8dc67dc7e77",
        @"\\?\usb#vid_0bb4&pid_0321&mi_00#e&ccef36c&0&0000#{e5323777-f976-4f5b-9b55-b94699c46e44}\global",
        @"\\?\usb#vid_35bd&pid_0202&mi_00#c&1c0768e5&0&0000#{e5323777-f976-4f5b-9b55-b94699c46e44}\global",
    ];

    [TestMethod]
    public void GetDeviceInstanceKey_StripsMonikerPrefixAndInterfaceClass()
    {
        Assert.AreEqual("usb#vid_0bb4&pid_0321&mi_00#e&ccef36c&0&0000",
            CameraIndexMapper.GetDeviceInstanceKey(DirectShowVftPath));
    }

    [TestMethod]
    public void GetDeviceInstanceKey_ReturnsNullForSoftwareDevices()
    {
        Assert.IsNull(CameraIndexMapper.GetDeviceInstanceKey(
            @"@device:sw:{860BB310-5D01-11D0-BD3B-00A0C911CE86}\{7BBFF097-B3FB-4B26-B685-7A998DE7CEAC}"));
    }

    [TestMethod]
    public void FindMatchingIndex_FindsSameDeviceAcrossInterfaceClasses()
    {
        Assert.AreEqual(1, CameraIndexMapper.FindMatchingIndex(DirectShowVftPath, MediaFoundationPaths));
    }

    [TestMethod]
    public void FindMatchingIndex_IgnoresCase()
    {
        Assert.AreEqual(1, CameraIndexMapper.FindMatchingIndex(DirectShowVftPath.ToUpperInvariant(), MediaFoundationPaths));
    }

    [TestMethod]
    public void FindMatchingIndex_ReturnsNullWhenDeviceIsMissing()
    {
        Assert.IsNull(CameraIndexMapper.FindMatchingIndex(DirectShowVftPath, MediaFoundationPaths[..1]));
    }
}
