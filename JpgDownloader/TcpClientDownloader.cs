using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace JpgDownloader
{
    public class TcpClientDownloader : BaseDownloader
    {
        public TcpClientDownloader(string ip, int port, string fileName, string downloadFolder)
            : base(ip, port, fileName, downloadFolder)
        {
        }

        protected override async Task<DownloadResult<string>> GetHtmlPageAsync(Uri pageUrl)
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
            var headers = await ReadHeaders(reader);
            var content = await reader.ReadToEndAsync();

            bool isSuccessfulStatusCode = statusLine.StatusCode / 100 == 2;
            return new DownloadResult<string>(
                statusLine.StatusCode,
                headers["Connection"],
                isSuccessfulStatusCode ? content : null
            );
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
                StatusCode: int.Parse(m.Groups[2].Value),
                CommentString: m.Groups[3].Value
            );
        }

        private static async Task<IDictionary<string, string>> ReadHeaders(StreamReader reader)
        {
            IDictionary<string, string> headersValues = new Dictionary<string, string>();
            var headerRegex = new Regex(@"(.+?):\s*(.+)");
            string line;
            while ((line = await reader.ReadLineAsync()) != string.Empty)
            {
                Match m = headerRegex.Match(line);
                headersValues.Add(m.Groups[1].Value, m.Groups[2].Value);
            }
            return headersValues;
        }

        protected override async Task<DownloadResult<byte[]>> GetJpgAsync(Uri jpgUrl)
        {
            TcpClient tcp = new TcpClient(jpgUrl.Host, jpgUrl.Port);
            using var socket = tcp.GetStream();
            using var writer = new StreamWriter(socket);

            var http = $"GET {jpgUrl.PathAndQuery} HTTP/1.1\r\n" +
                       $"Host: {jpgUrl.Host}\r\n" +
                       $"User-Agent: Own TCP client\r\n" +
                       $"Connection: close\r\n" +
                       $"Accept: image/jpeg\r\n" +
                       $"\r\n";
            writer.AutoFlush = true;
            await writer.WriteLineAsync(http);

            using var memoryBuffer = new MemoryStream();
            await socket.CopyToAsync(memoryBuffer);
            memoryBuffer.Seek(0, SeekOrigin.Begin);

            using var reader = new StreamReader(memoryBuffer);
            var statusLine = ParseStatusLine(await reader.ReadLineAsync());
            var headers = await ReadHeaders(reader);

            int contentLength = int.Parse(headers["Content-Length"]);
            byte[] imageData = new byte[contentLength];
            bool isSuccessfulStatusCode = statusLine.StatusCode / 100 == 2;
            if (isSuccessfulStatusCode)
            {
                memoryBuffer.Seek(-contentLength, SeekOrigin.End);
                memoryBuffer.Read(imageData, 0, contentLength);
            }

            return new DownloadResult<byte[]>(
                statusLine.StatusCode,
                headers["Connection"],
                isSuccessfulStatusCode ? imageData : null
            );
        }

        private record StatusLine(string HttpVersion, int StatusCode, string CommentString);
    }
}
