using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace JpgDownloader
{
    public record OperationStatus(
        string LastOperationResult,
        HttpStatusCode StatusCode
    );

    public abstract class BaseDownloader
    {
        private readonly Uri host;

        private readonly Uri pageUri;

        private readonly string downloadFolder;

        public BaseDownloader(string ip, int port, string fileName, string downloadFolder)
        {
            this.host = new Uri($"http://{ip}:{port}", UriKind.Absolute);
            this.pageUri = new Uri(this.host, fileName);
            this.downloadFolder = downloadFolder;
        }

        protected string DownloadFolder => this.downloadFolder;

        public async Task DownloadAsync(IProgress<OperationStatus> progress)
        {
            var html = await GetHtmlPageAsync(this.pageUri, progress)
                ?? throw new InvalidOperationException("Can't receive html file.");

            var jpgRegex = new Regex(@"<img.+?src=""(\S+?\.jpg)""");
            var jpgUrls = jpgRegex.Matches(html)
                .Select(m => m.Groups[1].Value)
                .Select(jpg => new Uri(jpg, UriKind.RelativeOrAbsolute))
                .ToList();

            Directory.CreateDirectory(this.DownloadFolder);
            Directory.GetFiles(this.DownloadFolder).ToList().ForEach(file => File.Delete(file));

            for (int i = 0; i < jpgUrls.Count; i++)
            {
                Uri jpgUrl = jpgUrls[i].IsAbsoluteUri ? jpgUrls[i] : new Uri(this.host, jpgUrls[i]);
                var imageBytes = await GetJpgAsync(jpgUrl, progress);
                if (imageBytes == null)
                {
                    continue;
                }
                var imageFileName = Path.Combine(this.DownloadFolder, Guid.NewGuid().ToString()) + ".jpg";

                await File.WriteAllBytesAsync(imageFileName, imageBytes);
            }
        }

        protected abstract Task<string?> GetHtmlPageAsync(Uri pageUrl, IProgress<OperationStatus> progress);

        protected abstract Task<byte[]?> GetJpgAsync(Uri jpgUrl, IProgress<OperationStatus> progress);

    }

    public class HttpClientDownloader : BaseDownloader
    {
        private readonly HttpClient httpClient;

        public HttpClientDownloader(string ip, int port, string fileName, string downloadFolder)
            : base(ip, port, fileName, downloadFolder)
        {
            this.httpClient = new HttpClient();
        }

        protected async override Task<string?> GetHtmlPageAsync(Uri pageUrl, IProgress<OperationStatus> progress)
        {
            var pageRequest = new HttpRequestMessage()
            {
                Method = HttpMethod.Get,
                RequestUri = pageUrl
            };
            var result = await this.httpClient.SendAsync(pageRequest);

            progress.Report(new OperationStatus("GET file", result.StatusCode));

            return result.IsSuccessStatusCode ? await result.Content.ReadAsStringAsync() : null;
        }

        protected async override Task<byte[]?> GetJpgAsync(Uri jpgUrl, IProgress<OperationStatus> progress)
        {
            var imageRequest = new HttpRequestMessage
            {
                RequestUri = jpgUrl
            };
            imageRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg"));
            var imageResponse = await this.httpClient.SendAsync(imageRequest);

            progress.Report(new OperationStatus($"GET jpg", imageResponse.StatusCode));

            return imageResponse.IsSuccessStatusCode ? await imageResponse.Content.ReadAsByteArrayAsync() : null;
        }
    }

    public class TcpClientDownloader : BaseDownloader
    {
        public TcpClientDownloader(string ip, int port, string fileName, string downloadFolder)
            : base(ip, port, fileName, downloadFolder)
        {
        }

        protected override async Task<string> GetHtmlPageAsync(Uri pageUrl, IProgress<OperationStatus> progress)
        {
            TcpClient tcp = new TcpClient(pageUrl.Host, pageUrl.Port);
            using var socket = tcp.GetStream();
            using var writer = new StreamWriter(socket);
            using var reader = new StreamReader(socket);

            var http = $"GET {pageUrl.PathAndQuery} HTTP/1.1\r\n" +
                       $"Host: {pageUrl.Host}\r\n" +
                       $"User-Agent: Own TCP client\r\n" +
                       $"Connection: close\r\n" +
                       $"Accept: text/html\r\n" +
                       $"\r\n";
            writer.AutoFlush = true;
            await writer.WriteLineAsync(http);

            var statusLine = ParseStatusLine(await reader.ReadLineAsync());
            bool isSuccessfulStatusCode = statusLine.ReturnCode / 100 == 2;
            if (!isSuccessfulStatusCode)
            {
                return null;
            }
            var headers = await ReadHeaders(reader);
            var content = await reader.ReadToEndAsync();

            return content;
        }

        private static StatusLine ParseStatusLine(string firstLine)
        {
            var regex = new Regex(@"(\S+) (\S+) (.+)");
            var m = regex.Match(firstLine);
            if (!m.Success)
            {
                throw new ArgumentException("Invalid first line.", nameof(firstLine));
            }
            return new StatusLine(
                HttpVersion: m.Groups[1].Value,
                ReturnCode: int.Parse(m.Groups[2].Value),
                CommentString: m.Groups[3].Value
            );
        }

        private static async Task<IDictionary<string, string>> ReadHeaders(StreamReader reader)
        {
            IDictionary<string, string> headersValues = new Dictionary<string, string>();
            var headerRegex = new Regex(@"(.+?):\s*(.+)");
            string line;
            while((line = await reader.ReadLineAsync()) != string.Empty)
            {
                Match m = headerRegex.Match(line);
                headersValues.Add(m.Groups[1].Value, m.Groups[2].Value);
            }
            return headersValues;
        }

        protected async override Task<byte[]> GetJpgAsync(Uri jpgUrl, IProgress<OperationStatus> progress)
        {
            TcpClient tcp = new TcpClient(jpgUrl.Host, jpgUrl.Port);
            using var socket = tcp.GetStream();
            using var writer = new StreamWriter(socket);

            var http = $"GET {jpgUrl.PathAndQuery} HTTP/1.1\r\n" +
                       $"Host: {jpgUrl.Host}\r\n" +
                       $"User-Agent: Own TCP client\r\n" +
                       $"Connection: close\r\n" +
                       $"Accept: text/html\r\n" +
                       $"\r\n";
            writer.AutoFlush = true;
            await writer.WriteLineAsync(http);

            using var memoryBuffer = new MemoryStream();
            await socket.CopyToAsync(memoryBuffer);
            memoryBuffer.Seek(0, SeekOrigin.Begin);

            using var reader = new StreamReader(memoryBuffer);
            var statusLine = ParseStatusLine(await reader.ReadLineAsync());
            bool isSuccessfulStatusCode = statusLine.ReturnCode / 100 == 2;
            if (!isSuccessfulStatusCode)
            {
                return null;
            }
            var headers = await ReadHeaders(reader);
            int contentLength = int.Parse(headers["Content-Length"]);
            byte[] imageData = new byte[contentLength];
            memoryBuffer.Seek(-contentLength, SeekOrigin.End);
            memoryBuffer.Read(imageData, 0, contentLength);

            return imageData;
        }

        record StatusLine(string HttpVersion, int ReturnCode, string CommentString);

    }

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
                this.statusLabel.Content = $"Last operation: {status.LastOperationResult}, " +
                                           $"last status code: {(int)status.StatusCode}";
            });
            try
            {
                string host = this.ipTextbox.Text;
                int port = int.Parse(this.portTextbox.Text);
                string relativePath = this.fileTextbox.Text;
                string downloadFolder = this.downloadFolderTextbox.Text;

                await new TcpClientDownloader(host, port, relativePath, downloadFolder)
                //await new HttpClientDownloader(host, port, relativePath, downloadFolder)
                    .DownloadAsync(progress);

                this.errorLabel.Content = string.Empty;
            }
            catch (Exception ex)
            {
                this.errorLabel.Content = $"Exception: {ex.Message}";
            }
        }
    }
}
