// Copyright 2015 Javier Flores Assad.
// All rights reserved.
// MIT license.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.ShareTarget;
using Windows.Storage;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace OneShareUX
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class ShareTargetPage : Page
    {
        private string ShareUrl { get; set; }
        private ShareOperation Operation { get; set; }
        private TelemetryClient telemetryClient; 

        public ShareTargetPage()
        {
            this.Operation = null;
            this.telemetryClient = new TelemetryClient();

            this.InitializeComponent();
            DataTransferManager.GetForCurrentView().DataRequested += Share_DataRequested;

            this.ButtonsVisibility = Visibility.Collapsed;
        }

        private Visibility ButtonsVisibility
        {
            get
            {
                return this.OpenButton.Visibility;
            }
            set
            {
                this.OpenButton.Visibility = value;
                this.CopyButton.Visibility = value;
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            Windows.UI.Core.SystemNavigationManager.GetForCurrentView().BackRequested += ShareTargetPage_BackRequested;

            if (e.Parameter is ShareOperation)
            {
                this.Operation = (ShareOperation)e.Parameter;
                await  this.ProcessShareTargetRequestAsync();
            }
            else if (e.Parameter is List<IStorageFile>)
            {
                List<IStorageFile> files = (List<IStorageFile>)e.Parameter;
                await TryUploadFilesToOneDriveAsync(files);
            }
            else
            {
                base.OnNavigatedTo(e);
            }
        }

        private void ShareTargetPage_BackRequested(object sender, Windows.UI.Core.BackRequestedEventArgs e)
        {
            var frame = ((Frame)Window.Current.Content);
            if (frame.CanGoBack)
            {
                frame.GoBack();
                e.Handled = true;
            }
        }

        private async Task ProcessShareTargetRequestAsync()
        {
            //this.Operation.ReportStarted();
            var stopWatch = new System.Diagnostics.Stopwatch();
            var eventTelemetry = new EventTelemetry("ShareTargetUpload");

            try
            {
                ulong totalSize = 0;

                List<IStorageFile> files = new List<IStorageFile>();

                IReadOnlyList<IStorageItem> sharedStorageItems = await this.Operation.Data.GetStorageItemsAsync();
                foreach (var item in sharedStorageItems)
                {
                    if (item.IsOfType(StorageItemTypes.File))
                    {
                        files.Add((IStorageFile)item);
                        totalSize += (await ((IStorageFile)item).GetBasicPropertiesAsync()).Size;
                    }
                }

                eventTelemetry.Metrics.Add("TotalFiles", files.Count);
                eventTelemetry.Metrics.Add("TotalSize", totalSize);

                stopWatch.Start();
                await TryUploadFilesToOneDriveAsync(files);
                eventTelemetry.Name = "ShareTargetUploadSuccess";
                eventTelemetry.Metrics.Add("TotalTimeInSeconds", stopWatch.ElapsedMilliseconds / 1000);
                this.telemetryClient.TrackEvent(eventTelemetry);
            }
            catch(Exception ex)
            {
                eventTelemetry.Name = "ShareTargetUploadFailure";
                eventTelemetry.Metrics.Add("TotalTimeInSeconds", stopWatch.ElapsedMilliseconds / 1000);
                eventTelemetry.Properties.Add("Failure", ex.Message);
                this.telemetryClient.TrackEvent(eventTelemetry);
            }
        }

        private async Task TryUploadFilesToOneDriveAsync(List<IStorageFile> files)
        {
            this.ProgressIndicator.IsActive = true;
            this.ProgressIndicator.Visibility = Visibility.Visible;

            try
            {
                this.ShareUrl = await UploadFilesToOneDriveAsync(files);
                this.ResultBlock.Text = "All done!, your OneDrive share link is ready!.";
                this.ButtonsVisibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                this.ResultBlock.Text = "Something went wrong!, check your Internet connection and try again.";
                this.telemetryClient.TrackException(ex);
                throw;
            }
            finally
            {
                this.ProgressIndicator.IsActive = false;
                this.ProgressIndicator.Visibility = Visibility.Collapsed;
            }
        }
        
        public async Task<string> UploadFilesToOneDriveAsync(List<IStorageFile> files)
        {
            string shareName = "OneDriveLinkApp/" + DateTime.Now.Ticks.ToString() + "/";

            var uploadRecord = await EasyUpload.UploadRecord.CreateFromFilesAsync("new", files);
            uploadRecord.OnUploadProgress += (sender, progress) =>
            {
                this.ProgressText.Text = string.Format("{0}%", progress);
            };

            var uploader = new EasyUpload.Uploader();
            await  uploader.AuthenticateClientAsync();
            await uploader.ContinueUploadAsync(shareName, uploadRecord);
            return uploadRecord.ShareUrl;
        }

        private void Share_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            args.Request.Data.Properties.Title = "OneDrive share";
            args.Request.Data.SetWebLink(new Uri(this.ShareUrl));
        }

        private void CopyButton_Tapped(object sender, TappedRoutedEventArgs e)
        {
            this.telemetryClient.TrackEvent("ShareTargetCopyButtonTapped");

            DataPackage pack = new DataPackage();
            pack.SetWebLink(new Uri(this.ShareUrl));
            pack.SetText(this.ShareUrl);
            Clipboard.SetContent(pack);

            if (this.Operation != null)
            {
                this.Operation.ReportCompleted();
                this.Operation = null;
            }
        }

        private async void OpenButton_Tapped(object sender, TappedRoutedEventArgs e)
        {
            this.telemetryClient.TrackEvent("ShareTargetOpenButtonTapped");

            await Windows.System.Launcher.LaunchUriAsync(new Uri(this.ShareUrl));

            if (this.Operation != null)
            {
                this.Operation.ReportCompleted();
                this.Operation = null;
            }
        }
    }
}
