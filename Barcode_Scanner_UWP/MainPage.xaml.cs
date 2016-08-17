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

using Windows.Graphics.Display;


namespace Barcode_Scanner_UWP
{

    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();

            if (String.Equals(Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamily, "Windows.Mobile"))
            {
                DisplayInformation.AutoRotationPreferences = DisplayOrientations.Portrait;
            }
        }

        private async void App_Suspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            await barcodecontrol.Cleanup();
            BarcodePopup.IsOpen = false;
            deferral.Complete();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            Application.Current.Suspending += App_Suspending;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            Application.Current.Suspending -= App_Suspending;
        }

        void BarcodeFound(string barcode)
        {
            txtBarcode.Text = barcode;
            BarcodePopup.IsOpen = false;
        }

        void OnError(Exception e)
        {

        }

        private async void btnOpen_Click(object sender, RoutedEventArgs e)
        {
            BarcodePopup.IsOpen = true;
            await barcodecontrol.StartScan(BarcodeFound, OnError);
        }


    }
}
