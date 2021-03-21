using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace JpgDownloader
{
    public record OperationStatus(
        string ConnectionStatus,
        string LastOperationType,
        int StatusCode
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
            var getPageResult = await GetHtmlPageAsync(this.pageUri);

            progress.Report(new OperationStatus(
                getPageResult.Connection,
                "GET page",
                getPageResult.StatusCode
            ));

            if (getPageResult.Result == null)
            {
                throw new InvalidOperationException("Can't receive html file.");
            }

            var html = getPageResult.Result;
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
                var jpgResult = await GetJpgAsync(jpgUrl);

                progress.Report(new OperationStatus(
                    jpgResult.Connection,
                    "GET jpg",
                    jpgResult.StatusCode
                ));

                if (jpgResult.Result == null)
                {
                    continue;
                }

                var imageFileName = Path.Combine(this.DownloadFolder, Guid.NewGuid().ToString()) + ".jpg";

                await File.WriteAllBytesAsync(imageFileName, jpgResult.Result);
            }
        }

        protected abstract Task<DownloadResult<string>> GetHtmlPageAsync(Uri pageUrl);

        protected abstract Task<DownloadResult<byte[]>> GetJpgAsync(Uri jpgUrl);

        protected record DownloadResult<T>(int StatusCode, string Connection, T? Result);
    }
}
