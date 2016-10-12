// Copyright 2015 Javier Flores Assad.
// All rights reserved.
// MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace OneShareUX
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private string ShareUrl = string.Empty;
        private TelemetryClient telemetryClient = new TelemetryClient();


        public MainPage()
        {
            this.InitializeComponent();
            DataTransferManager.GetForCurrentView().DataRequested += Share_DataRequested;
        }

        private bool IsUploadInProgress
        {
            set
            {
                if (value)
                {
                    this.UploadProgressRing.IsActive = true;
                    this.PickButton.IsEnabled = false;
                    this.PickButton.Opacity = 0.5;
                    this.ProgressText.Visibility = Visibility.Visible;
                    this.UploadCountText.Visibility = Visibility.Visible;
                }
                else
                {
                    this.UploadProgressRing.IsActive = false;
                    this.PickButton.IsEnabled = true;
                    this.PickButton.Opacity = 1.0;
                    this.ProgressText.Visibility = Visibility.Collapsed;
                    this.UploadCountText.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void Share_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            this.telemetryClient.TrackEvent("MainShareDataRequested");
            args.Request.Data.Properties.Title = "OneDrive Share";
            args.Request.Data.SetWebLink(new Uri(this.ShareUrl));
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            Windows.UI.Core.SystemNavigationManager.GetForCurrentView().BackRequested += MainPage_BackRequested;
        }

        private void MainPage_BackRequested(object sender, Windows.UI.Core.BackRequestedEventArgs e)
        {
            Application.Current.Exit();
        }

        private async void PickButton_Tapped(object sender, TappedRoutedEventArgs e)
        {
            this.ShareButton.IsEnabled = false;
            this.CopyButton.IsEnabled = false;
            await this.PickFilesUploadAndShareAsync();
        }

        private async Task PickFilesUploadAndShareAsync()
        {
            this.StatusText.Text = string.Empty;
            this.ProgressText.Text = string.Empty;
            this.UploadCountText.Text = string.Empty;

            var uploader = new EasyUpload.Uploader();

            try
            {
                await uploader.AuthenticateClientAsync();
            }
            catch (Exception)
            {
                this.StatusText.Text = "We were not able to obtain OneDrive access. Check your internet connection and try again.";
                return;
            }

            var filePicker = new FileOpenPicker();
            filePicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            filePicker.FileTypeFilter.Add("*");

            var pickedFiles = await filePicker.PickMultipleFilesAsync();
            if (pickedFiles.Count() == 0)
            {
                return;
            }

            this.IsUploadInProgress = true;
            this.StatusText.Text = "Please wait while we upload the files you chose.";

            List<IStorageFile> files = new List<IStorageFile>();
            ulong totalSize = 0;
            foreach (var pickedFile in pickedFiles)
            {
                totalSize += (await pickedFile.GetBasicPropertiesAsync()).Size;
                files.Add(pickedFile);
            }

            string shareName = "OneDriveLinkApp/" + DateTime.Now.Ticks.ToString() + "/";

            var stopWatch = new System.Diagnostics.Stopwatch();
            stopWatch.Start();

            var eventTelemetry = new EventTelemetry("MainUpload");
            eventTelemetry.Metrics.Add("TotalFiles", files.Count());
            eventTelemetry.Metrics.Add("TotalSize", totalSize);

            try
            {
                var uploadRecord = await EasyUpload.UploadRecord.CreateFromFilesAsync("new", files);
                uploadRecord.OnUploadProgress += (sender, progress) =>
                {
                    this.ProgressText.Text = string.Format("({0}%)", progress);
                };

                await uploader.ContinueUploadAsync(shareName, uploadRecord);
                this.ShareUrl = uploadRecord.ShareUrl;
                eventTelemetry.Name = "MainUploadSuccess";
                eventTelemetry.Metrics.Add("TotalTimeInSeconds", stopWatch.ElapsedMilliseconds / 1000);
                this.telemetryClient.TrackEvent(eventTelemetry);
            }
            catch (Exception ex)
            {
                this.StatusText.Text = "Something went wrong while uploading to OneDrive. Check your internet connection and try again.";
                eventTelemetry.Name = "MainUploadFailure";
                eventTelemetry.Metrics.Add("TotalTimeInSeconds", stopWatch.ElapsedMilliseconds / 1000);
                eventTelemetry.Properties.Add("Failure", ex.Message);
                this.telemetryClient.TrackEvent(eventTelemetry);
                this.telemetryClient.TrackException(ex);
                return;
            }
            finally
            {
                this.IsUploadInProgress = false;
            }

            if (!string.IsNullOrEmpty(this.ShareUrl))
            {
                this.StatusText.Text = "All done!. your OneDrive share is ready to share with anyone. Use the buttons at the bottom bar.";
                this.ShareButton.IsEnabled = true;
                this.CopyButton.IsEnabled = true;
            }
        }

        private void CopyButton_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(this.ShareUrl))
            {
                PickButton_Tapped(sender, e);
                return;
            }

            this.telemetryClient.TrackEvent("MainCopyButtonTapped");

            DataPackage pack = new DataPackage();
            pack.SetWebLink(new Uri(this.ShareUrl));
            pack.SetText(this.ShareUrl);
            Clipboard.SetContent(pack);
        }

        private void ShareButton_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(this.ShareUrl))
            {
                PickButton_Tapped(sender, e);
                return;
            }

            this.telemetryClient.TrackEvent("MainShareButtonTapped");

            DataTransferManager.ShowShareUI();
        }

        private async void AboutButton_Tapped(object sender, TappedRoutedEventArgs e)
        {
            this.telemetryClient.TrackEvent("MainAboutButtonTapped");
            await Windows.System.Launcher.LaunchUriAsync(new Uri("http://easyupload.azurewebsites.net/privacy.html"));
        }
    }
}