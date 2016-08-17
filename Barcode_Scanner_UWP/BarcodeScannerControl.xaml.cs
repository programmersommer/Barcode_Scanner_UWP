using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

using System.Threading;
using Windows.Media.Capture;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.MediaProperties;
using Windows.Graphics.Imaging;
using System.Diagnostics;
using Windows.Storage.Streams;
using Windows.Media.Devices;
using Windows.Devices.Enumeration;
using ZXing;


// Forked from https://github.com/stepheUp/VideoScanZXingWinRT
// Thank you, Stéphanie!

namespace Barcode_Scanner_UWP
{
    public sealed partial class BarcodeScannerControl : UserControl
    {
        // could be replaced with: internal Func<string, Task> OnBarCodeFound
        internal Action<string> OnBarCodeFound
        {
            get;
            private set;
        }

        internal Action<Exception> OnError
        {
            get;
            private set;
        }

        MediaCapture mediaCapture;
        private DispatcherTimer timerFocus;

        SemaphoreSlim _semRender = new SemaphoreSlim(1);
        SemaphoreSlim _semScan = new SemaphoreSlim(1);
        bool isStillFocusing = false;

        double _width = 640;
        double _height = 480;

        bool _cleanedUp = true;
        bool isInitialized = false;
        bool _processScan = true;

        internal BarcodeReader _ZXingReader;

        public BarcodeScannerControl()
        {
            this.InitializeComponent();

            timerFocus = new DispatcherTimer();
        }

        #region capturing photo

        async Task initCamera()
        {
            if (isInitialized == true) return;

            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);

            DeviceInformation frontCamera = null;
            DeviceInformation rearCamera = null;

            foreach (var device in devices)
            {
                switch (device.EnclosureLocation.Panel)
                {
                    case Windows.Devices.Enumeration.Panel.Front:
                        frontCamera = device;
                        break;
                    case Windows.Devices.Enumeration.Panel.Back:
                        rearCamera = device;
                        break;
                }
            }

            try
            {

                if (rearCamera != null)
                {
                    await mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings { VideoDeviceId = rearCamera.Id });
                }
                else if (frontCamera != null)
                {
                    await mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings { VideoDeviceId = frontCamera.Id }); 
                }
                else { }

                isInitialized = true;
                await SetResolution();
                if (mediaCapture.VideoDeviceController.FlashControl.Supported) mediaCapture.VideoDeviceController.FlashControl.Auto = false;
            }
            catch { }
        }


        async Task SetResolution()
        {
            System.Collections.Generic.IReadOnlyList<IMediaEncodingProperties> res;
            res = mediaCapture.VideoDeviceController.GetAvailableMediaStreamProperties(MediaStreamType.VideoPreview);
            uint maxResolution = 0;
            int indexMaxResolution = 0;

            if (res.Count >= 1)
            {
                for (int i = 0; i < res.Count; i++)
                {
                    VideoEncodingProperties vp = (VideoEncodingProperties)res[i];

                    if (vp.Width > maxResolution)
                    {
                        indexMaxResolution = i;
                        maxResolution = vp.Width;
                        _width = vp.Width;
                        _height = vp.Height;
                    }
                }
                await mediaCapture.VideoDeviceController.SetMediaStreamPropertiesAsync(MediaStreamType.VideoPreview, res[indexMaxResolution]);
            }
        }


        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SetGridSize();
        }


        private void SetGridSize()
        {
            VideoCaptureElement.Height = previewGrid.Height - 100;
            VideoCaptureElement.Width = previewGrid.Width;
        }

        #endregion


        #region Barcode scanner

        private void btnBarcodeCancel_Click(object sender, RoutedEventArgs e)
        {
            if (OnBarCodeFound != null)
            {
                OnBarCodeFound("");
            }
        }


        public async Task Cleanup()
        {
            if (!_cleanedUp)       // Free all - NECESSARY TO CLEANUP PROPERLY !
            {
                _processScan = false;
                timerFocus.Stop();
                timerFocus.Tick -= timerFocus_Tick;

                await mediaCapture.StopPreviewAsync();
                mediaCapture.FocusChanged -= mediaCaptureManager_FocusChanged;

                _cleanedUp = true;
            }
        }



        async Task StartPreview()
        {
            if (String.Equals(Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamily, "Windows.Mobile"))
            {
                mediaCapture.SetPreviewRotation(VideoRotation.Clockwise90Degrees);
                mediaCapture.SetPreviewMirroring(true);
            }

            var focusControl = mediaCapture.VideoDeviceController.FocusControl;
            if (!focusControl.FocusChangedSupported)
            {
                if (focusControl.Supported)
                {
                    _cleanedUp = false;
                    _processScan = true;

                    mediaCapture.FocusChanged += mediaCaptureManager_FocusChanged;
                    VideoCaptureElement.Source = mediaCapture;
                    VideoCaptureElement.Stretch = Stretch.UniformToFill;
                    await mediaCapture.StartPreviewAsync();
                    await focusControl.UnlockAsync();

                    focusControl.Configure(new FocusSettings { Mode = FocusMode.Auto });
                    timerFocus.Tick += timerFocus_Tick;
                    timerFocus.Interval = new TimeSpan(0, 0, 3);
                    timerFocus.Start();
                }
                else
                {
                    OnErrorAsync(new Exception("AutoFocus control is not supported on this device"));
                }
            }
            else
            {
                _cleanedUp = false;
                _processScan = true;

                mediaCapture.FocusChanged += mediaCaptureManager_FocusChanged;
                VideoCaptureElement.Source = mediaCapture;
                VideoCaptureElement.Stretch = Stretch.UniformToFill;
                await mediaCapture.StartPreviewAsync();
                await focusControl.UnlockAsync();
                var settings = new FocusSettings { Mode = FocusMode.Continuous, AutoFocusRange = AutoFocusRange.FullRange };
                focusControl.Configure(settings);
                await focusControl.FocusAsync();
            }


        }

        private async void mediaCaptureManager_FocusChanged(MediaCapture sender, MediaCaptureFocusChangedEventArgs args)
        {
            if (_processScan)
            {
                await CapturePhotoFromCameraAsync();
            }
        }

        private async void timerFocus_Tick(object sender, object e)
        {
            if (isStillFocusing) return; // if camera is still focusing

            if (_processScan)
            {
                isStillFocusing = true;

                await mediaCapture.VideoDeviceController.FocusControl.FocusAsync();
                await CapturePhotoFromCameraAsync();

                isStillFocusing = false;
            }
        }

        async Task CapturePhotoFromCameraAsync()
        {
            if (!_processScan) return;

            if (await _semRender.WaitAsync(0) == true)
            {
                try
                {
                    VideoFrame videoFrame = new VideoFrame(BitmapPixelFormat.Bgra8, (int)_width, (int)_height);
                    await mediaCapture.GetPreviewFrameAsync(videoFrame);

                    var bytes = await SaveSoftwareBitmapToBufferAsync(videoFrame.SoftwareBitmap);
                    await ScanImageAsync(bytes);
                }
                finally
                {
                    _semRender.Release();
                }
            }
        }

        private async Task<byte[]> SaveSoftwareBitmapToBufferAsync(SoftwareBitmap softwareBitmap)
        {
            byte[] bytes = null;

            try
            {
                IRandomAccessStream stream = new InMemoryRandomAccessStream();
                {

                    // Create an encoder with the desired format
                    BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.BmpEncoderId, stream);
                    encoder.SetSoftwareBitmap(softwareBitmap);
                    encoder.IsThumbnailGenerated = false;
                    await encoder.FlushAsync();

                    bytes = new byte[stream.Size];

                    // This returns IAsyncOperationWithProgess, so you can add additional progress handling
                    await stream.ReadAsync(bytes.AsBuffer(), (uint)stream.Size, Windows.Storage.Streams.InputStreamOptions.None);
                }
            }

            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }

            return bytes;
        }


        private void OnBarCodeFoundAsync(string barcode)
        {
            timerFocus.Stop();
            _processScan = false;

            if (OnBarCodeFound != null)
            {
                OnBarCodeFound(barcode);
            }
        }


        private void OnErrorAsync(Exception e)
        {
            OnError(e);
        }

        private async Task ScanImageAsync(byte[] pixelsArray)
        {
            await _semScan.WaitAsync();
            try
            {
                if (_processScan)
                {
                    var result = ScanBitmap(pixelsArray, (int)_width, (int)_height);
                    if (result != null)
                    {
                        OnBarCodeFoundAsync(result.Text);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);     // Wasn't able to find a barcode    
            }
            finally
            {
                _semScan.Release();
            }
        }

        internal Result ScanBitmap(byte[] pixelsArray, int width, int height)
        {
            var result = _ZXingReader.Decode(pixelsArray, width, height, BitmapFormat.Unknown);

            if (result != null)
            {
                Debug.WriteLine("ScanBitmap : " + result.Text);
            }

            return result;
        }

        internal BarcodeReader GetReader()
        {
            return new BarcodeReader()
            {
                AutoRotate = true,
                Options = new ZXing.Common.DecodingOptions() { TryHarder = false, PossibleFormats = new BarcodeFormat[] { BarcodeFormat.All_1D, BarcodeFormat.QR_CODE } }
            };
        }

        #endregion


        private async void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            await Cleanup();
        }


        public async Task StartScan(Action<string> onBarCodeFound, Action<Exception> onError)
        {
            btnBarcodeCancel.IsEnabled = false;
            mediaCapture = new MediaCapture();
            isInitialized = false;
            _processScan = false;

            await initCamera();

            if (isInitialized == false) return;

            OnBarCodeFound = onBarCodeFound;
            OnError = onError;

            _ZXingReader = GetReader();

            await StartPreview();
            btnBarcodeCancel.IsEnabled = true;
        }



    }
}
