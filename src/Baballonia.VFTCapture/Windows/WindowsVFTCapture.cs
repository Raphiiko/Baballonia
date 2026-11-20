using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Capture = Baballonia.SDK.Capture;

namespace Baballonia.VFTCapture.Windows;

/// <summary>
/// Vive Facial Tracker camera capture
/// </summary>
public sealed class WindowsVftCapture(string source, ILogger logger) : Capture(source, logger)
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

    private readonly Mat _originalMat = new();
    private readonly object _captureLock = new();
    private VideoCapture? _videoCapture;
    private volatile bool _loop;

    /// <summary>
    /// Starts video capture and applies custom resolution and framerate settings.
    /// </summary>
    /// <returns>True if the video capture started successfully, otherwise false.</returns>
    public override async Task<bool> StartCapture()
    {
        Logger.LogDebug("Starting VFT camera capture...");

        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
        {
            try
            {
                _videoCapture = await Task.Run(OpenCapture, cts.Token);

                _loop = true;
                _ = Task.Run(VideoCapture_UpdateLoop);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to start VFT camera capture");
                IsReady = false;
                return IsReady;
            }
        }

        IsReady = _videoCapture!.IsOpened();
        Logger.LogDebug("VFT camera capture started successfully: " + IsReady);
        return IsReady;
    }

    /// <summary>
    /// Activates the tracker and opens its camera.
    /// </summary>
    private VideoCapture OpenCapture()
    {
        SetTrackerState(setActive: true);

        VideoCapture capture;
        if (int.TryParse(Source, out var index))
            capture = VideoCapture.FromCamera(index, VideoCaptureAPIs.MSMF);
        else
            capture = new VideoCapture(Source);

        capture.ConvertRgb = false;
        return capture;
    }

    /// <summary>
    /// Releases a capture that stopped delivering frames and opens a new one.
    /// Never retry Read on a dead capture without this delay: a failed MSMF read returns at once and leaks inside OpenCV.
    /// </summary>
    private void Reconnect()
    {
        Logger.LogWarning("VFT camera stopped sending frames, reconnecting in {Delay}s...", ReconnectDelay.TotalSeconds);

        lock (_captureLock)
            ReleaseCapture();

        Thread.Sleep(ReconnectDelay);
        if (!_loop)
            return;

        var capture = OpenCapture();
        lock (_captureLock)
        {
            if (_loop)
            {
                _videoCapture = capture;
                return;
            }
        }

        capture.Release();
        capture.Dispose();
    }

    private void ReleaseCapture()
    {
        _videoCapture?.Release();
        _videoCapture?.Dispose();
        _videoCapture = null;
    }

    private Task VideoCapture_UpdateLoop()
    {
        Mat yuyvMat = new();
        Mat yuvConvert = new();
        while (_loop)
        {
            try
            {
                IsReady = _videoCapture?.Read(_originalMat) == true;
                if (!IsReady)
                {
                    Reconnect();
                    continue;
                }

                yuyvMat = Mat.FromPixelData(400, 400, MatType.CV_8UC2, _originalMat.Data);

                yuvConvert = yuyvMat.CvtColor(ColorConversionCodes.YUV2GRAY_Y422, 0);
                yuvConvert = yuvConvert.ColRange(VFTCommon.ColumnRange);
                yuvConvert = yuvConvert.Resize(VFTCommon.ImageSize);
                yuvConvert = yuvConvert.GaussianBlur(VFTCommon.GaussianBlurSize, 0);

                var rawMat = yuvConvert.LUT(VFTCommon.Lut);
                SetRawMat(rawMat);
            }
            // catch (TaskCanceledException)
            // {
            //     return;
            // }
            catch (Exception)
            {
                // ignored
            }
        }
        yuyvMat.Dispose();
        yuvConvert.Dispose();

        return Task.CompletedTask;
    }

    private void SetTrackerState(bool setActive)
    {
        if (setActive)
            WindowsUsbCommunicator.activate_tracker(0);
        else
            WindowsUsbCommunicator.deactivate_tracker(0);
    }

    /// <summary>
    /// Stops video capture and cleans up resources.
    /// </summary>
    /// <returns>True if capture stopped successfully, otherwise false.</returns>
    public override Task<bool> StopCapture()
    {
        Logger.LogDebug("Stopping VFT camera capture...");

        if (!_loop)
        {
            Logger.LogDebug("VFT camera capture is not running, returning false");
            return Task.FromResult(false);
        }

        lock (_captureLock)
        {
            _loop = false;
            IsReady = false;
            ReleaseCapture();
        }
        SetTrackerState(false);
        Logger.LogDebug("VFT camera capture stopped successfully");
        return Task.FromResult(true);
    }
}
