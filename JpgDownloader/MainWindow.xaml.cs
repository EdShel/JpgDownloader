using System;
using System.Windows;

namespace JpgDownloader
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void downloadButton_Click(object sender, RoutedEventArgs e)
        {
            var progress = new Progress<OperationStatus>(status =>
            {
                this.statusLabel.Content = $"Connection: {status.ConnectionStatus}, " +
                                           $"Last operation: {status.LastOperationType}, " +
                                           $"last status code: {status.StatusCode}";
            });
            try
            {
                string host = this.ipTextbox.Text;
                int port = int.Parse(this.portTextbox.Text);
                string relativePath = this.fileTextbox.Text;
                string downloadFolder = this.downloadFolderTextbox.Text;

                BaseDownloader downloader = sender == this.downloadHttpButton
                    ? new HttpClientDownloader(host, port, relativePath, downloadFolder)
                    : new TcpClientDownloader(host, port, relativePath, downloadFolder);

                await downloader.DownloadAsync(progress);

                this.errorLabel.Content = string.Empty;
            }
            catch (Exception ex)
            {
                this.errorLabel.Content = $"Exception: {ex.Message}";
            }
        }
    }
}
