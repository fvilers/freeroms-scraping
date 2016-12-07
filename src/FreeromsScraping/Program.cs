﻿using Fizzler.Systems.HtmlAgilityPack;
using FreeromsScraping.Configuration;
using HtmlAgilityPack;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FreeromsScraping
{
    internal class Program
    {
        private static async Task MainAsync()
        {
            var configuration = (ScrapingSection)ConfigurationManager.GetSection("scraping");

            foreach (var source in configuration.Sources.Cast<SourceElement>())
            {
                Logger.Info($"Downloading catalog from source {source.Name}...");
                await DownloadCatalogAsync(source.Name, source.Url, configuration.DestinationFolder).ConfigureAwait(false);
            }

            Logger.Info("END");
            Console.Read();
        }

        private static async Task DownloadCatalogAsync(string name, string url, string destinationFolder)
        {
            var menuPage = await GetContentAsStringAsync(url).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(menuPage))
            {
                return;
            }

            foreach (var catalogLink in ParseContentForMenuLink(menuPage))
            {
                var listPage = await GetContentAsStringAsync(catalogLink).ConfigureAwait(false);
                if (String.IsNullOrWhiteSpace(listPage))
                {
                    continue;
                }

                foreach (var romLink in ParseContentForRomLink(listPage))
                {
                    var romPage = await GetContentAsStringAsync(romLink).ConfigureAwait(false);
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
                    Logger.Info($"Downloading game {fileName}...");
                    var path = Path.Combine(folder, fileName);
                    var bytes = await GetContentAsBytesAsync(fileLink).ConfigureAwait(false);
                    File.WriteAllBytes(path, bytes);
                }
            }
        }

        private static string ParseContentForFileLink(string html)
        {
            var regex = new Regex(@"document.getElementById\(""romss""\)\.innerHTML='&nbsp;<a href=""(?<link>http:\/\/download\.freeroms\.com\/\w+\/(?:\w|\.|-|,|!|\(|\)|\+)+)"">Direct&nbsp;Download<\/a>&nbsp;';", RegexOptions.Compiled);
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

        private static async Task<byte[]> GetContentAsBytesAsync(string url)
        {
            using (var client = new HttpClient())
            {
                var response = await client.GetAsync(url).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    Logger.Error($"Error while fetching {url} !");
                    return null;
                }

                var content = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                return content;
            }
        }

        private static async Task<string> GetContentAsStringAsync(string url)
        {
            using (var client = new HttpClient())
            {
                var response = await client.GetAsync(url).ConfigureAwait(false);

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
