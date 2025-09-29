using System.Text.RegularExpressions;
using System.Web;
using API.MangaDownloadClients;
using API.Schema.MangaContext;
using HtmlAgilityPack;

// ReSharper disable StringLiteralTypo

namespace API.MangaConnectors;

public sealed class Weebcentral : MangaConnector
{
    public Weebcentral() : base(
        "Weebcentral",
        ["en"],
        [
            "https://weebcentral.com"
        ],
        "https://weebcentral.com/favicon.ico"
    )
    {
        downloadClient = new HttpDownloadClient();
    }

    // ============================ SEARCH ============================
    public override (Manga, MangaConnectorId<Manga>)[] SearchManga(string mangaSearchName)
    {
        Uri baseUri = new(BaseUris[0]);
        Uri searchUrl = new(baseUri,
            "search/data?sort=Best+Match&order=Descending&official=Any&anime=Any&adult=Any&display_mode=Minimal+Display&text=" +
            HttpUtility.UrlEncode(mangaSearchName));

        RequestResult res = downloadClient.MakeRequest(searchUrl.ToString(), RequestType.Default);
        if ((int)res.statusCode < 200 || (int)res.statusCode >= 300)
            return [];

        using StreamReader sr = new(res.result);
        string html = sr.ReadToEnd();

        HtmlDocument doc = new();
        doc.LoadHtml(html);

        HtmlNodeCollection? anchors =
            doc.DocumentNode.SelectNodes("/article/a[@class='link link-hover tooltip tooltip-bottom']");
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract Apparently it does return null. Ask AgilityPack why the return type isnt marked as such...
        if (anchors is null || anchors.Count < 1)
            return [];

        List<(Manga, MangaConnectorId<Manga>)> list = anchors
            .Select(a => a.GetAttributeValue("href", ""))
            .Where(href => !string.IsNullOrEmpty(href))
            .Select(href => new Uri(baseUri, href).ToString())
            .Select(GetMangaFromUrl)
            .Where(manga => manga is not null)
            .Select(manga => ((Manga, MangaConnectorId<Manga>))manga!)
            .ToList();

        return list.ToArray();
    }

    // ======================== URL → Manga ===========================
    public override (Manga, MangaConnectorId<Manga>)? GetMangaFromUrl(string url)
    {
        Match m = SeriesUrl.Match(url);
        return !m.Success ? null : GetMangaFromId($"{m.Groups["id"].Value}/{m.Groups["slug"].Value}");
    }

    // ======================== ID → Manga ============================
    public override (Manga, MangaConnectorId<Manga>)? GetMangaFromId(string mangaIdOnSite)
    {
        string[] parts = mangaIdOnSite.Split('/', 2);
        if (parts.Length != 2)
            return null;

        string id = parts[0];
        string slug = parts[1];

        string url = $"{BaseUris[0]}/series/{id}/{slug}/";
        RequestResult res = downloadClient.MakeRequest(url, RequestType.MangaInfo);
        if ((int)res.statusCode < 200 || (int)res.statusCode >= 300)
            return null;

        using StreamReader sr = new(res.result);
        string html = sr.ReadToEnd();

        HtmlDocument doc = new();
        doc.LoadHtml(html);

        string title = doc.DocumentNode.SelectSingleNode("//h1").InnerText.Trim();

        string cover = doc.DocumentNode
            .SelectSingleNode("//section[@class='flex items-center justify-center']/picture/img")
            .GetAttributeValue("src", string.Empty);

        if (!string.IsNullOrEmpty(cover))
            cover = MakeAbsoluteUrl(new Uri(url), cover);

        string description =
            doc.DocumentNode.SelectSingleNode("//ul/li[strong/text() = 'Description']/p").InnerText.Trim();

        // === STATUS ===
        MangaReleaseStatus status = MapStatus(
            doc.DocumentNode.SelectSingleNode("//ul/li[strong/text() = 'Status: ']/a").InnerText.Trim()
        );

        List<Author> authors = doc.DocumentNode.SelectNodes("//ul/li[strong/text() = 'Author(s): ']/span")
            .Select(n => new Author(n.InnerText.Trim())).ToList();

        List<MangaTag> tags = doc.DocumentNode.SelectNodes("//ul/li[strong/text() = 'Tags(s): ']/span")
            .Select(n => new MangaTag(n.InnerText.Trim())).ToList();

        List<AltTitle> altTitles = doc.DocumentNode.SelectNodes("//ul/li[strong/text() = 'Associated Name(s)']/ul/li")
            .Select(n => new AltTitle("en", n.InnerText.Trim())).ToList();

        Manga m = new(
            HtmlEntity.DeEntitize(title).Trim(),
            description,
            cover,
            status,
            authors,
            tags,
            [],
            altTitles,
            originalLanguage: "en");
        MangaConnectorId<Manga> mcId = new(m,
            this,
            $"{id}/{slug}",
            $"{BaseUris[0]}/series/{id}/{slug}/");
        m.MangaConnectorIds.Add(mcId);
        return (m, mcId);
    }

    // ========================== CAPITOLI ============================
    public override (Chapter, MangaConnectorId<Chapter>)[] GetChapters(MangaConnectorId<Manga> mangaId,
        string? language = null)
    {
        string[] parts = mangaId.IdOnConnectorSite.Split('/', 2);
        if (parts.Length != 2)
            return [];

        string id = parts[0];
        string seriesUrl = $"{BaseUris[0]}/series/{id}/full-chapter-list";

        RequestResult res = downloadClient.MakeRequest(seriesUrl, RequestType.Default);
        if ((int)res.statusCode < 200 || (int)res.statusCode >= 300)
            return [];

        using StreamReader sr = new(res.result);
        string html = sr.ReadToEnd();
        if (string.IsNullOrEmpty(html))
            return [];

        HtmlDocument doc = new();
        doc.LoadHtml(html);

        List<(Chapter, MangaConnectorId<Chapter>)> chapters =
            ParseChaptersFromHtml(mangaId.Obj, doc, new Uri(BaseUris[0]));

        // Ordinamento finale: Volume → Capitolo (numerico)
        return chapters
            .OrderBy(c => c.Item1, new Chapter.ChapterComparer())
            .ToArray();
    }

    // ===================== IMMAGINI CAPITOLO =======================
    internal override string[] GetChapterImageUrls(MangaConnectorId<Chapter> chapterId)
    {
        string url = chapterId.WebsiteUrl ?? $"{BaseUris[0]}/chapters/{chapterId.IdOnConnectorSite}";

        RequestResult res = downloadClient.MakeRequest(url, RequestType.MangaInfo);
        if ((int)res.statusCode < 200 || (int)res.statusCode >= 300)
            return [];

        using StreamReader sr = new(res.result);
        string html = sr.ReadToEnd();

        HtmlDocument doc = new();
        doc.LoadHtml(html);

        HtmlNodeCollection imageNodes = doc.DocumentNode.SelectNodes($"//section[@hx-get='{url}/images']/img");

        return imageNodes.Select(imgNode => imgNode.GetAttributeValue("src", "")).ToArray();
    }

    // ============================ PARSER CAPITOLI ===================
    private static readonly Regex ChapterRex = new(@"(\d+(?:\.\d+)*)", RegexOptions.Compiled);
    private static readonly Regex IdRex = new(@"chapters\/(?<id>\w*)", RegexOptions.Compiled);

    private List<(Chapter, MangaConnectorId<Chapter>)> ParseChaptersFromHtml(Manga manga, HtmlDocument document,
        Uri baseUri)
    {
        // main wrapper
        HtmlNode? chaptersWrapper = document.DocumentNode.SelectSingleNode("/html/body");

        List<(Chapter, MangaConnectorId<Chapter>)> ret = [];

        foreach (HtmlNode ch in chaptersWrapper.Descendants("a"))
        {
            // Get and parse URL to find ID
            string url = ch.GetAttributeValue("href", "") ?? "Undefined";

            if (!url.StartsWith("https://") && !url.StartsWith("http://"))
                continue;

            Match idMatch = IdRex.Match(url);
            string? id = idMatch.Success ? idMatch.Groups["id"].Value : null;
            if (id is null)
                continue;


            // Get and parse chapter number and name
            string chapterNode = ch.SelectSingleNode("span[@class='grow flex items-center gap-2']/span")?.InnerText ??
                                 "Undefined";

            // Chapter number
            MatchCollection chapterNumberMatch = ChapterRex.Matches(chapterNode);
            string chapterNumber = chapterNumberMatch.Count > 0 ? chapterNumberMatch[^1].Groups[1].Value : "-1";

            Chapter chapter = new(manga, chapterNumber, null);
            MangaConnectorId<Chapter> chId = new(chapter, this, id,
                MakeAbsoluteUrl(baseUri, url));

            ret.Add((chapter, chId));
        }

        return ret;
    }

    // ============================ HELPERS ===========================
    private static readonly Regex SeriesUrl = new(@"https?:\/\/[^/]+\/series\/(?<id>[^/]+)\/(?<slug>[^/]+)\/?",
        RegexOptions.IgnoreCase);

    private static string MakeAbsoluteUrl(Uri baseUri, string s)
    {
        s = s.Trim();
        if (s.StartsWith("//"))
            return "https:" + s;
        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return s;
        return new Uri(baseUri, s).ToString();
    }

    // ===================== STATUS (estrazione + mapping) =============
    private static MangaReleaseStatus MapStatus(string s)
    {
        return s.Trim().ToLowerInvariant() switch
        {
            "ongoing" or "attivo" => MangaReleaseStatus.Continuing,
            "complete" => MangaReleaseStatus.Completed,
            "hiatus" => MangaReleaseStatus.OnHiatus,
            "cancelled" => MangaReleaseStatus.Cancelled,
            _ => MangaReleaseStatus.Unreleased
        };
    }
}