﻿using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HtmlAgilityPack;
using Logging;
using Newtonsoft.Json;
using PuppeteerSharp;
using Tranga.TrangaTasks;

namespace Tranga.Connectors;

public class Mangasee : Connector
{
    public override string name { get; }
    private IBrowser? browser = null;

    public Mangasee(string downloadLocation, string imageCachePath, Logger? logger) : base(downloadLocation,
        imageCachePath, logger)
    {
        this.name = "Mangasee";
        this.downloadClient = new DownloadClient(new Dictionary<byte, int>()
        {
            { (byte)1, 60 }
        }, logger);

        Task d = new Task(DownloadBrowser);
        d.Start();
    }

    private async void DownloadBrowser()
    {
        logger?.WriteLine(this.GetType().ToString(), "Downloading headless browser");
        BrowserFetcher browserFetcher = new BrowserFetcher();
        double last = 0;
        browserFetcher.DownloadProgressChanged += async (sender, args) =>
        {
            double current = Convert.ToDouble(args.BytesReceived) / Convert.ToDouble(args.TotalBytesToReceive);
            if (args.TotalBytesToReceive == args.BytesReceived)
            {
                logger?.WriteLine(this.GetType().ToString(), "Browser downloaded. Launching...");
            }
            else if (current > last + 0.01)
            {
                logger?.WriteLine(this.GetType().ToString(), $"Browser progress: {current:P2}");
                last = current;
            }

        };
        if (!browserFetcher.CanDownloadAsync(BrowserFetcher.DefaultChromiumRevision).Result)
        {
            logger?.WriteLine(this.GetType().ToString(), "Can't download");
            return;
        }
        await browserFetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision);
        this.browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            ExecutablePath = browserFetcher.GetExecutablePath(BrowserFetcher.DefaultChromiumRevision)
        });
    }

    public override Publication[] GetPublications(string publicationTitle = "")
    {
        logger?.WriteLine(this.GetType().ToString(), $"Getting Publications (title={publicationTitle})");
        string sanitizedTitle = string.Concat(Regex.Matches(publicationTitle, "[A-z]* *")).ToLower().Replace(' ', '+');
        string requestUrl = $"https://mangasee123.com/_search.php";
        DownloadClient.RequestResult requestResult =
            downloadClient.MakeRequest(requestUrl, (byte)1);
        if (requestResult.statusCode != HttpStatusCode.OK)
            return Array.Empty<Publication>();

        return ParsePublicationsFromHtml(requestResult.result, publicationTitle);
    }

    private Publication[] ParsePublicationsFromHtml(Stream html, string publicationTitle)
    {
        string jsonString = new StreamReader(html).ReadToEnd();
        List<SearchResultItem> result = JsonConvert.DeserializeObject<List<SearchResultItem>>(jsonString)!;
        Dictionary<SearchResultItem, int> queryFiltered = new();
        foreach (SearchResultItem resultItem in result)
        {
            foreach (string term in publicationTitle.Split(' '))
                if (resultItem.i.Contains(term, StringComparison.CurrentCultureIgnoreCase))
                    if (!queryFiltered.TryAdd(resultItem, 0))
                        queryFiltered[resultItem]++;
        }

        queryFiltered = queryFiltered.Where(item => item.Value >= publicationTitle.Split(' ').Length - 1)
            .ToDictionary(item => item.Key, item => item.Value);

        HashSet<Publication> ret = new();
        List<SearchResultItem> orderedFiltered =
            queryFiltered.OrderBy(item => item.Value).ToDictionary(item => item.Key, item => item.Value).Keys.ToList();

        foreach (SearchResultItem orderedItem in orderedFiltered)
        {
            DownloadClient.RequestResult requestResult =
                downloadClient.MakeRequest($"https://mangasee123.com/manga/{orderedItem.i}", (byte)1);
            if (requestResult.statusCode != HttpStatusCode.OK)
                return Array.Empty<Publication>();
            ret.Add(ParseSinglePublicationFromHtml(requestResult.result, orderedItem.s, orderedItem.i, orderedItem.a));
        }
        return ret.ToArray();
    }

    
    private Publication ParseSinglePublicationFromHtml(Stream html, string sortName, string publicationId, string[] a)
    {
        StreamReader reader = new (html);
        string htmlString = reader.ReadToEnd();
        HtmlDocument document = new ();
        document.LoadHtml(htmlString);

        string originalLanguage = "", status = "";
        Dictionary<string, string> altTitles = new(), links = new();
        HashSet<string> tags = new();

        HtmlNode posterNode =
            document.DocumentNode.Descendants("img").First(img => img.HasClass("img-fluid") && img.HasClass("bottom-5"));
        string posterUrl = posterNode.GetAttributeValue("src", "");
        string coverFileNameInCache = SaveCoverImageToCache(posterUrl, 1);

        HtmlNode attributes = document.DocumentNode.Descendants("div")
            .First(div => div.HasClass("col-md-9") && div.HasClass("col-sm-8") && div.HasClass("top-5"))
            .Descendants("ul").First();

        HtmlNode[] authorsNodes = attributes.Descendants("li")
            .First(node => node.InnerText.Contains("author(s):", StringComparison.CurrentCultureIgnoreCase))
            .Descendants("a").ToArray();
        string[] authors = new string[authorsNodes.Length];
        for (int j = 0; j < authors.Length; j++)
            authors[j] = authorsNodes[j].InnerText;
        string author = string.Join(" - ", authors);

        HtmlNode[] genreNodes = attributes.Descendants("li")
            .First(node => node.InnerText.Contains("genre(s):", StringComparison.CurrentCultureIgnoreCase))
            .Descendants("a").ToArray();
        foreach (HtmlNode genreNode in genreNodes)
            tags.Add(genreNode.InnerText);

        HtmlNode yearNode = attributes.Descendants("li")
            .First(node => node.InnerText.Contains("released:", StringComparison.CurrentCultureIgnoreCase))
            .Descendants("a").First();
        int year = Convert.ToInt32(yearNode.InnerText);

        HtmlNode[] statusNodes = attributes.Descendants("li")
            .First(node => node.InnerText.Contains("status:", StringComparison.CurrentCultureIgnoreCase))
            .Descendants("a").ToArray();
        foreach(HtmlNode statusNode in statusNodes)
            if (statusNode.InnerText.Contains("publish", StringComparison.CurrentCultureIgnoreCase))
                status = statusNode.InnerText.Split(' ')[0];
        
        HtmlNode descriptionNode = attributes.Descendants("li").First(node => node.InnerText.Contains("description:", StringComparison.CurrentCultureIgnoreCase)).Descendants("div").First();
        string description = descriptionNode.InnerText;
        
        int i = 0;
        foreach(string at in a)
            altTitles.Add((i++).ToString(), at);
        
        return new Publication(sortName, author, description, altTitles, tags.ToArray(), posterUrl, coverFileNameInCache, links,
            year, originalLanguage, status, publicationId);
    }
    
    private class SearchResultItem
    {
        public string i { get; set; }
        public string s { get; set; }
        public string[] a { get; set; }
    }

    public override Chapter[] GetChapters(Publication publication, string language = "")
    {
        XDocument doc = XDocument.Load($"https://mangasee123.com/rss/{publication.publicationId}.xml");
        XElement[] chapterItems = doc.Descendants("item").ToArray();
        List<Chapter> ret = new();
        foreach (XElement chapter in chapterItems)
        {
            string? volumeNumber = null;
            string chapterName = chapter.Descendants("title").First().Value;
            string parseFrom = name.Replace(publication.sortName, "", StringComparison.InvariantCultureIgnoreCase);
            string chapterNumber = Regex.Matches(chapterName, "[0-9]+")[^1].ToString();

            string url = chapter.Descendants("link").First().Value;
            url = url.Replace(Regex.Matches(url,"(-page-[0-9])")[0].ToString(),"");
            ret.Add(new Chapter(chapterName, volumeNumber, chapterNumber, url));
        }

        ret.Reverse();
        return ret.ToArray();
    }

    public override void DownloadChapter(Publication publication, Chapter chapter, DownloadChapterTask parentTask)
    {
        while (this.browser is null)
        {
            logger?.WriteLine(this.GetType().ToString(), "Waiting for headless browser to download...");
            Thread.Sleep(1000);
        }

        IPage page = browser.NewPageAsync().Result;
        IResponse response = page.GoToAsync(chapter.url).Result;
        if (response.Ok)
        {
            HtmlDocument document = new ();
            document.LoadHtml(page.GetContentAsync().Result);

            HtmlNode gallery = document.DocumentNode.Descendants("div").First(div => div.HasClass("ImageGallery"));
            HtmlNode[] images = gallery.Descendants("img").Where(img => img.HasClass("img-fluid")).ToArray();
            List<string> urls = new();
            foreach(HtmlNode galleryImage in images)
                urls.Add(galleryImage.GetAttributeValue("src", ""));
            
            string comicInfoPath = Path.GetTempFileName();
            File.WriteAllText(comicInfoPath, GetComicInfoXmlString(publication, chapter, logger));
        
            DownloadChapterImages(urls.ToArray(), GetArchiveFilePath(publication, chapter), (byte)1, parentTask, comicInfoPath);
        }
    }
}