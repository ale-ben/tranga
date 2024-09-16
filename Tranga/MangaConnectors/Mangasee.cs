﻿using System.Data;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Soenneker.Utils.String.NeedlemanWunsch;
using Tranga.Jobs;

namespace Tranga.MangaConnectors;

public class Mangasee : MangaConnector
{
    public Mangasee(GlobalBase clone) : base(clone, "Mangasee", ["en"])
    {
        this.downloadClient = new ChromiumDownloadClient(clone);
    }

    private struct SearchResult
    {
        public string i { get; set; }
        public string s { get; set; }
        public string[] a { get; set; }
    }

    public override Manga[] GetManga(string publicationTitle = "")
    {
        Log($"Searching Publications. Term=\"{publicationTitle}\"");
        string requestUrl = "https://mangasee123.com/_search.php";
        RequestResult requestResult =
            downloadClient.MakeRequest(requestUrl, RequestType.Default);
        if ((int)requestResult.statusCode < 200 || (int)requestResult.statusCode >= 300)
        {
            Log($"Failed to retrieve search: {requestResult.statusCode}");
            return Array.Empty<Manga>();
        }
        
        try
        {
            SearchResult[] searchResults = JsonConvert.DeserializeObject<SearchResult[]>(requestResult.htmlDocument!.DocumentNode.InnerText) ??
                                           throw new NoNullAllowedException();
            SearchResult[] filteredResults = FilteredResults(publicationTitle, searchResults);
            Log($"Total available manga: {searchResults.Length} Filtered down to: {filteredResults.Length}");
            

            string[] urls = filteredResults.Select(result => $"https://mangasee123.com/manga/{result.i}").ToArray();
            List<Manga> searchResultManga = new();
            foreach (string url in urls)
            {
                Manga? newManga = GetMangaFromUrl(url);
                if(newManga is { } manga)
                    searchResultManga.Add(manga);
            }
            Log($"Retrieved {searchResultManga.Count} publications. Term=\"{publicationTitle}\"");
            return searchResultManga.ToArray();
        }
        catch (NoNullAllowedException)
        {
            Log("Failed to retrieve search");
            return Array.Empty<Manga>();
        }
    }

    private readonly string[] _filterWords = {"a", "the", "of", "as", "to", "no", "for", "on", "with", "be", "and", "in", "wa", "at", "be", "ni"};
    private string ToFilteredString(string input) => string.Join(' ', input.ToLower().Split(' ').Where(word => _filterWords.Contains(word) == false));
    private SearchResult[] FilteredResults(string publicationTitle, SearchResult[] unfilteredSearchResults)
    {
        Dictionary<SearchResult, int> similarity = new();
        foreach (SearchResult sr in unfilteredSearchResults)
        {
            List<int> scores = new();
            string filteredPublicationString = ToFilteredString(publicationTitle);
            string filteredSString = ToFilteredString(sr.s);
            scores.Add(NeedlemanWunschStringUtil.CalculateSimilarity(filteredSString, filteredPublicationString));
            foreach (string srA in sr.a)
            {
                string filteredAString = ToFilteredString(srA);
                scores.Add(NeedlemanWunschStringUtil.CalculateSimilarity(filteredAString, filteredPublicationString));
            }
            similarity.Add(sr, scores.Sum() / scores.Count);
        }

        List<SearchResult> ret = similarity.OrderBy(s => s.Value).Take(10).Select(s => s.Key).ToList();
        return ret.ToArray();
    }

    public override Manga? GetMangaFromId(string publicationId)
    {
        return GetMangaFromUrl($"https://mangasee123.com/manga/{publicationId}");
    }

    public override Manga? GetMangaFromUrl(string url)
    {
        Regex publicationIdRex = new(@"https:\/\/mangasee123.com\/manga\/(.*)(\/.*)*");
        string publicationId = publicationIdRex.Match(url).Groups[1].Value;

        RequestResult requestResult = this.downloadClient.MakeRequest(url, RequestType.MangaInfo);
        if((int)requestResult.statusCode < 300 && (int)requestResult.statusCode >= 200 && requestResult.htmlDocument is not null)
            return ParseSinglePublicationFromHtml(requestResult.htmlDocument, publicationId, url);
        return null;
    }

    private Manga ParseSinglePublicationFromHtml(HtmlDocument document, string publicationId, string websiteUrl)
    {
        string originalLanguage = "", status = "";
        Dictionary<string, string> altTitles = new(), links = new();
        HashSet<string> tags = new();
        Manga.ReleaseStatusByte releaseStatus = Manga.ReleaseStatusByte.Unreleased;

        HtmlNode posterNode = document.DocumentNode.SelectSingleNode("//div[@class='BoxBody']//div[@class='row']//img");
        string posterUrl = posterNode.GetAttributeValue("src", "");
        string coverFileNameInCache = SaveCoverImageToCache(posterUrl, publicationId, RequestType.MangaCover);

        HtmlNode titleNode = document.DocumentNode.SelectSingleNode("//div[@class='BoxBody']//div[@class='row']//h1");
        string sortName = titleNode.InnerText;

        HtmlNode[] authorsNodes = document.DocumentNode
            .SelectNodes("//div[@class='BoxBody']//div[@class='row']//span[text()='Author(s):']/..").Descendants("a")
            .ToArray();
        List<string> authors = new();
        foreach (HtmlNode authorNode in authorsNodes)
            authors.Add(authorNode.InnerText);

        HtmlNode[] genreNodes = document.DocumentNode
            .SelectNodes("//div[@class='BoxBody']//div[@class='row']//span[text()='Genre(s):']/..").Descendants("a")
            .ToArray();
        foreach (HtmlNode genreNode in genreNodes)
            tags.Add(genreNode.InnerText);

        HtmlNode yearNode = document.DocumentNode
            .SelectNodes("//div[@class='BoxBody']//div[@class='row']//span[text()='Released:']/..").Descendants("a")
            .First();
        int year = Convert.ToInt32(yearNode.InnerText);

        HtmlNode[] statusNodes = document.DocumentNode
            .SelectNodes("//div[@class='BoxBody']//div[@class='row']//span[text()='Status:']/..").Descendants("a")
            .ToArray();
        foreach (HtmlNode statusNode in statusNodes)
            if (statusNode.InnerText.Contains("publish", StringComparison.CurrentCultureIgnoreCase))
                status = statusNode.InnerText.Split(' ')[0];
        switch (status.ToLower())
        {
            case "cancelled": releaseStatus = Manga.ReleaseStatusByte.Cancelled; break;
            case "hiatus": releaseStatus = Manga.ReleaseStatusByte.OnHiatus; break;
            case "discontinued": releaseStatus = Manga.ReleaseStatusByte.Cancelled; break;
            case "complete": releaseStatus = Manga.ReleaseStatusByte.Completed; break;
            case "ongoing": releaseStatus = Manga.ReleaseStatusByte.Continuing; break;
        }

        HtmlNode descriptionNode = document.DocumentNode
            .SelectNodes("//div[@class='BoxBody']//div[@class='row']//span[text()='Description:']/..")
            .Descendants("div").First();
        string description = descriptionNode.InnerText;

        Manga manga = new(sortName, authors.ToList(), description, altTitles, tags.ToArray(), posterUrl,
            coverFileNameInCache, links,
            year, originalLanguage, publicationId, releaseStatus, websiteUrl: websiteUrl);
        AddMangaToCache(manga);
        return manga;
    }

    public override Chapter[] GetChapters(Manga manga, string language="en")
    {
        Log($"Getting chapters {manga}");
        try
        {
            XDocument doc = XDocument.Load($"https://mangasee123.com/rss/{manga.publicationId}.xml");
            XElement[] chapterItems = doc.Descendants("item").ToArray();
            List<Chapter> chapters = new();
            Regex chVolRex = new(@".*chapter-([0-9\.]+)(?:-index-([0-9\.]+))?.*");
            foreach (XElement chapter in chapterItems)
            {
                string url = chapter.Descendants("link").First().Value;
                Match m = chVolRex.Match(url);
                string? volumeNumber = m.Groups[2].Success ? m.Groups[2].Value : "1";
                string chapterNumber = m.Groups[1].Value;

                string chapterUrl = Regex.Replace(url, @"-page-[0-9]+(\.html)", ".html");
                chapters.Add(new Chapter(manga, "", volumeNumber, chapterNumber, chapterUrl));
            }

            //Return Chapters ordered by Chapter-Number
            Log($"Got {chapters.Count} chapters. {manga}");
            return chapters.Order().ToArray();
        }
        catch (HttpRequestException e)
        {
            Log($"Failed to load https://mangasee123.com/rss/{manga.publicationId}.xml \n\r{e}");
            return Array.Empty<Chapter>();
        }
    }

    public override HttpStatusCode DownloadChapter(Chapter chapter, ProgressToken? progressToken = null)
    {
        if (progressToken?.cancellationRequested ?? false)
        {
            progressToken.Cancel();
            return HttpStatusCode.RequestTimeout;
        }

        Manga chapterParentManga = chapter.parentManga;
        if (progressToken?.cancellationRequested ?? false)
        {
            progressToken.Cancel();
            return HttpStatusCode.RequestTimeout;
        }

        Log($"Retrieving chapter-info {chapter} {chapterParentManga}");

        RequestResult requestResult = this.downloadClient.MakeRequest(chapter.url, RequestType.Default);
        if (requestResult.htmlDocument is null)
        {
            progressToken?.Cancel();
            return HttpStatusCode.RequestTimeout;
        }

        HtmlDocument document = requestResult.htmlDocument;
        
        HtmlNode gallery = document.DocumentNode.Descendants("div").First(div => div.HasClass("ImageGallery"));
        HtmlNode[] images = gallery.Descendants("img").Where(img => img.HasClass("img-fluid")).ToArray();
        List<string> urls = new();
        foreach(HtmlNode galleryImage in images)
            urls.Add(galleryImage.GetAttributeValue("src", ""));
            
        string comicInfoPath = Path.GetTempFileName();
        File.WriteAllText(comicInfoPath, chapter.GetComicInfoXmlString());
        
        return DownloadChapterImages(urls.ToArray(), chapter.GetArchiveFilePath(), RequestType.MangaImage, comicInfoPath, progressToken:progressToken);
    }
}