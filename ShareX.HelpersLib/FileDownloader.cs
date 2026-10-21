#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX.HelpersLib
{
    public class FileDownloader
    {
        public event Action FileSizeReceived;
        public event Action ProgressChanged;

        public string URL { get; set; }
        public string DownloadLocation { get; set; }
        public string AcceptHeader { get; set; }

        public bool IsDownloading { get; private set; }
        public bool IsCanceled { get; private set; }
        public long FileSize { get; private set; } = -1;
        public long DownloadedSize { get; private set; }
        public double DownloadSpeed { get; private set; }

        public double DownloadPercentage
        {
            get
            {
                if (FileSize > 0)
                {
                    return (double)DownloadedSize / FileSize * 100;
                }

                return 0;
            }
        }

        private const int bufferSize = 32768;
        private readonly object downloadLock = new object();
        private CancellationTokenSource downloadCancellation;

        public FileDownloader()
        {
        }

        public FileDownloader(string url, string downloadLocation)
        {
            URL = url;
            DownloadLocation = downloadLocation;
        }

        public async Task<bool> StartDownload()
        {
            CancellationTokenSource cancellation;

            lock (downloadLock)
            {
                if (IsDownloading || string.IsNullOrEmpty(URL))
                {
                    return false;
                }

                IsDownloading = true;
                IsCanceled = false;
                FileSize = -1;
                DownloadedSize = 0;
                DownloadSpeed = 0;
                cancellation = new CancellationTokenSource();
                downloadCancellation = cancellation;
            }

            try
            {
                return await DoWork(cancellation.Token);
            }
            finally
            {
                lock (downloadLock)
                {
                    downloadCancellation = null;
                    IsDownloading = false;
                    cancellation.Dispose();
                }
            }
        }

        public void StopDownload()
        {
            lock (downloadLock)
            {
                if (IsDownloading)
                {
                    IsCanceled = true;
                    downloadCancellation.Cancel();
                }
            }
        }

        private async Task<bool> DoWork(CancellationToken cancellationToken)
        {
            bool fileCreated = false;
            try
            {
                HttpClient client = HttpClientFactory.Create();

                using (HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Get, URL))
                {
                    if (!string.IsNullOrEmpty(AcceptHeader))
                    {
                        requestMessage.Headers.Accept.ParseAdd(AcceptHeader);
                    }

                    using (HttpResponseMessage responseMessage = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                    {
                        responseMessage.EnsureSuccessStatusCode();

                        FileSize = responseMessage.Content.Headers.ContentLength ?? -1;

                        FileSizeReceived?.Invoke();

                        Stopwatch timer = Stopwatch.StartNew();
                        Stopwatch progressEventTimer = Stopwatch.StartNew();
                        long speedTest = 0;
                        byte[] buffer = new byte[bufferSize];

                        cancellationToken.ThrowIfCancellationRequested();

                        using (Stream responseStream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken))
                        using (FileStream fileStream = new FileStream(DownloadLocation, FileMode.Create, FileAccess.Write, FileShare.Read))
                        {
                            fileCreated = true;

                            while (FileSize < 0 || DownloadedSize < FileSize)
                            {
                                int count = FileSize < 0 ? buffer.Length : (int)Math.Min(buffer.Length, FileSize - DownloadedSize);
                                int bytesRead = await responseStream.ReadAsync(buffer, 0, count, cancellationToken);

                                if (bytesRead == 0)
                                {
                                    if (FileSize >= 0 && DownloadedSize < FileSize)
                                    {
                                        throw new EndOfStreamException("The download ended before the declared content length was reached.");
                                    }

                                    break;
                                }

                                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                                DownloadedSize += bytesRead;
                                speedTest += bytesRead;

                                if (timer.ElapsedMilliseconds > 500)
                                {
                                    DownloadSpeed = (double)speedTest / timer.ElapsedMilliseconds * 1000;
                                    speedTest = 0;
                                    timer.Restart();
                                }

                                if (progressEventTimer.ElapsedMilliseconds > 100)
                                {
                                    ProgressChanged?.Invoke();
                                    progressEventTimer.Restart();
                                }
                            }

                            ProgressChanged?.Invoke();
                            cancellationToken.ThrowIfCancellationRequested();
                        }

                        return true;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                if (IsCanceled && fileCreated)
                {
                    try
                    {
                        if (File.Exists(DownloadLocation))
                        {
                            File.Delete(DownloadLocation);
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }
    }
}