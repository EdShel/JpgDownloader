using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace JpgDownloader
{
    public class HttpClientDownloader : BaseDownloader
    {
        private readonly HttpClient httpClient;

        public HttpClientDownloader(string ip, int port, string fileName, string downloadFolder)
            : base(ip, port, fileName, downloadFolder)
        {
            this.httpClient = new HttpClient();
        }

        protected override async Task<DownloadResult<string>> GetHtmlPageAsync(Uri pageUrl)
        {
            var pageRequest = new HttpRequestMessage()
            {
                Method = HttpMethod.Get,
                RequestUri = pageUrl
            };
            var result = await this.httpClient.SendAsync(pageRequest);

            return new DownloadResult<string>(
                (int)result.StatusCode,
                result.Headers.Connection.FirstOrDefault(),
                result.IsSuccessStatusCode ? await result.Content.ReadAsStringAsync() : null
            );
        }

        protected override async Task<DownloadResult<byte[]>> GetJpgAsync(Uri jpgUrl)
        {
            var imageRequest = new HttpRequestMessage
            {
                RequestUri = jpgUrl
            };
            imageRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg"));
            var imageResponse = await this.httpClient.SendAsync(imageRequest);

            return new DownloadResult<byte[]>(
                (int)imageResponse.StatusCode,
                imageResponse.Headers.Connection.FirstOrDefault(),
                imageResponse.IsSuccessStatusCode ? await imageResponse.Content.ReadAsByteArrayAsync() : null
            );
        }
    }
}
