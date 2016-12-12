using Fizzler.Systems.HtmlAgilityPack;
using FreeromsScraping.Configuration;
using FreeromsScraping.IO;
using HtmlAgilityPack;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FreeromsScraping
{
    internal class Program
    {
        private static readonly HttpClient Client = new HttpClient();

        private static async Task MainAsync()
        {
            var configuration = (ScrapingSection)ConfigurationManager.GetSection("scraping");

            foreach (var source in configuration.Sources.Cast<SourceElement>())
            {
                Logger.Info($"Downloading catalog from source {source.Name}...");
                await DownloadCatalogAsync(source.Name, source.Url, configuration.DestinationFolder).ConfigureAwait(false);
            }

            Client?.Dispose();

            Logger.Info("END");
            Console.Read();
        }

        private static async Task DownloadCatalogAsync(string name, string url, string destinationFolder)
        {
            var menuPage = await RetryHelper.ExecuteAndThrowAsync(() => GetContentAsStringAsync(url), e => true).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(menuPage))
            {
                return;
            }

            foreach (var catalogLink in ParseContentForMenuLink(menuPage))
            {
                var listPage = await RetryHelper.ExecuteAndThrowAsync(() => GetContentAsStringAsync(catalogLink), e => true).ConfigureAwait(false);
                if (String.IsNullOrWhiteSpace(listPage))
                {
                    continue;
                }

                foreach (var romLink in ParseContentForRomLink(listPage))
                {
                    var romPage = await RetryHelper.ExecuteAndThrowAsync(() => GetContentAsStringAsync(romLink), e => true).ConfigureAwait(false);
                    if (String.IsNullOrWhiteSpace(romPage))
                    {
                        continue;
                    }

                    var fileLink = ParseContentForFileLink(romPage);
                    if (String.IsNullOrWhiteSpace(fileLink))
                    {
                        continue;
                    }

                    var folder = Path.Combine(destinationFolder, name);
                    if (!Directory.Exists(folder))
                    {
                        Directory.CreateDirectory(folder);
                    }

                    var fileName = Path.GetFileName(fileLink);
                    var path = Path.Combine(folder, fileName);
                    if (File.Exists(path))
                    {
                        Logger.Info("--> File already exists, skipping.");
                        continue;
                    }

                    await RetryHelper.ExecuteAndThrowAsync(() => SaveContentAsync(fileLink, path), e => true).ConfigureAwait(false);
                }
            }
        }

        private static string ParseContentForFileLink(string html)
        {
            var regex = new Regex(@"document\.getElementById\(""romss""\)\.innerHTML='&nbsp;<a href=""(?<link>http:\/\/(?:\w|\d)+\.freeroms\.com\/(?:\/|\w|\.|-|,|!|\(|\)|\+|\[|\]|%)+)"">Direct&nbsp;Download<\/a>&nbsp;';", RegexOptions.Compiled);
            var match = regex.Match(html);

            if (!match.Success)
            {
                Logger.Error("No link to rom found in html !");
                return null;
            }

            return match.Groups["link"].Value;
        }

        private static IEnumerable<string> ParseContentForRomLink(string html)
        {
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);

            foreach (var node in htmlDoc.DocumentNode.QuerySelectorAll("a[href*='rom_download.php']"))
            {
                yield return node.Attributes["href"].Value;
            }
        }

        private static async Task SaveContentAsync(string url, string path)
        {
            using (var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    Logger.Error($"Error while fetching {url} !");
                    return;
                }

                Console.Write($"Downloading file {url}... ");
                var left = Console.CursorLeft;
                var top = Console.CursorTop;
                var fileSize = response.Content.Headers.ContentLength;


                using (var stream = await RetryHelper.ExecuteAndThrowAsync(() => response.Content.ReadAsStreamAsync(), e => true).ConfigureAwait(false))
                {
                    using (var destination = new FileStream(path, FileMode.Create))
                    {
                        var sw = new Stopwatch();
                        var progress = new SynchronousProgress<long>(value =>
                        {
                            Console.CursorLeft = left;
                            Console.CursorTop = top;
                            var speed = value / sw.Elapsed.TotalSeconds / 1024;

                            if (fileSize.HasValue)
                            {
                                var pct = (decimal)(value * 100) / fileSize;
                                Console.Write($"{pct:0.00} % @ {speed:0.00} kb/s");
                            }
                            else
                            {
                                Console.Write($"{value} bytes @ {speed:0.00} kb/s");
                            }
                        });

                        sw.Start();
                        Console.CursorVisible = false;
                        await stream.CopyToAsync(destination, progress, 8192);
                        Console.CursorVisible = true;
                        Console.WriteLine();
                        sw.Stop();
                    }
                }
            }
        }

        private static async Task<string> GetContentAsStringAsync(string url)
        {
            using (var response = await Client.GetAsync(url).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    Logger.Error($"Error while fetching {url} !");
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return content;
            }
        }

        private static IEnumerable<string> ParseContentForMenuLink(string html)
        {
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);

            foreach (var node in htmlDoc.DocumentNode.QuerySelectorAll("tr.letters a"))
            {
                yield return node.Attributes["href"].Value;
            }
        }

        private static void Main()
        {
            AsyncContext.Run(async () => await MainAsync().ConfigureAwait(false));
        }
    }
}
