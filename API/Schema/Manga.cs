using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using API.MangaDownloadClients;
using API.Schema.Jobs;
using API.Schema.MangaConnectors;
using Microsoft.EntityFrameworkCore;
using static System.IO.UnixFileMode;
using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;

namespace API.Schema;

[PrimaryKey("MangaId")]
public class Manga(
    string connectorId,
    string name,
    string description,
    string websiteUrl,
    string coverUrl,
    string? coverFileNameInCache,
    uint year,
    string? originalLanguage,
    MangaReleaseStatus releaseStatus,
    float ignoreChapterBefore,
    MangaConnector mangaConnector,
    ICollection<Author> authors,
    ICollection<MangaTag> tags,
    ICollection<Link> links,
    ICollection<MangaAltTitle> altTitles)
{
    [MaxLength(64)]
    public string MangaId { get; init; } = TokenGen.CreateToken(typeof(Manga), 64);
    [MaxLength(64)]
    public string ConnectorId { get; init; } = connectorId;

    public string Name { get; internal set; } = name;
    public string Description { get; internal set; } = description;
    public string WebsiteUrl { get; internal set; } = websiteUrl;
    public string CoverUrl { get; internal set; } = coverUrl;
    public string? CoverFileNameInCache { get; internal set; } = coverFileNameInCache;
    public uint year { get; internal set; } = year;
    public string? OriginalLanguage { get; internal set; } = originalLanguage;
    public MangaReleaseStatus ReleaseStatus { get; internal set; } = releaseStatus;
    public string FolderName { get; private set; } = BuildFolderName(name);
    public float IgnoreChapterBefore { get; internal set; } = ignoreChapterBefore;

    [ForeignKey("MangaConnectorId")]
    public MangaConnector MangaConnector { get; private set; } = mangaConnector;
    
    public ICollection<Author> Authors { get; internal set; } = authors;
    
    public ICollection<MangaTag> Tags { get; internal set; } = tags;
    
    public ICollection<Link> Links { get; internal set; } = links;
    
    public ICollection<MangaAltTitle> AltTitles { get; internal set; } = altTitles;

    public Manga(string connectorId, string name, string description, string websiteUrl, string coverUrl,
        string? coverFileNameInCache,
        uint year, string? originalLanguage, MangaReleaseStatus releaseStatus, float ignoreChapterBefore)
        : this(connectorId, name, description, websiteUrl, coverUrl, coverFileNameInCache, year, originalLanguage,
            releaseStatus,
            ignoreChapterBefore, null, null, null, null, null)
    {
        
    }

    public MoveFileOrFolderJob UpdateFolderName(string downloadLocation, string newName)
    {
        string oldName = this.FolderName;
        this.FolderName = newName;
        return new MoveFileOrFolderJob(Path.Join(downloadLocation, oldName), Path.Join(downloadLocation, this.FolderName));
    }

    internal void UpdateWithInfo(Manga other)
    {
        this.Name = other.Name;
        this.year = other.year;
        this.Description = other.Description;
        this.CoverUrl = other.CoverUrl;
        this.OriginalLanguage = other.OriginalLanguage;
        this.Authors = other.Authors;
        this.Links = other.Links;
        this.Tags = other.Tags;
        this.AltTitles = other.AltTitles;
        this.ReleaseStatus = other.ReleaseStatus;
    }

    private static string BuildFolderName(string mangaName)
    {
        return mangaName;
    }
    
    internal string SaveCoverImageToCache()
    {
        Regex urlRex = new (@"https?:\/\/((?:[a-zA-Z0-9-]+\.)+[a-zA-Z0-9]+)\/(?:.+\/)*(.+\.([a-zA-Z]+))");
        //https?:\/\/[a-zA-Z0-9-]+\.([a-zA-Z0-9-]+\.[a-zA-Z0-9]+)\/(?:.+\/)*(.+\.([a-zA-Z]+)) for only second level domains
        Match match = urlRex.Match(CoverUrl);
        string filename = $"{match.Groups[1].Value}-{MangaId}.{match.Groups[3].Value}";
        string saveImagePath = Path.Join(TrangaSettings.coverImageCache, filename);

        if (File.Exists(saveImagePath))
            return saveImagePath;
        
        RequestResult coverResult = new HttpDownloadClient().MakeRequest(CoverUrl, RequestType.MangaCover);
        using MemoryStream ms = new();
        coverResult.result.CopyTo(ms);
        Directory.CreateDirectory(TrangaSettings.coverImageCache);
        File.WriteAllBytes(saveImagePath, ms.ToArray());
        return saveImagePath;
    }
    
    public string CreatePublicationFolder()
    {
        string publicationFolder = Path.Join(TrangaSettings.downloadLocation, this.FolderName);
        if(!Directory.Exists(publicationFolder))
            Directory.CreateDirectory(publicationFolder);
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            File.SetUnixFileMode(publicationFolder, GroupRead | GroupWrite | GroupExecute | OtherRead | OtherWrite | OtherExecute | UserRead | UserWrite | UserExecute);
        return publicationFolder;
    }

    public string GetRSS(Chapter[] chapters)
    {
        var feed = new SyndicationFeed(Name, Description, new Uri(WebsiteUrl));
        feed.Description = new TextSyndicationContent(Description);
        feed.Language = OriginalLanguage;
        feed.Links.Add(new SyndicationLink(new Uri(WebsiteUrl)));
        
        foreach (var link in Links)
        {
            feed.Links.Add(new SyndicationLink(new Uri(link.LinkUrl)));
        }
        
        foreach (var author in Authors)
        {
            feed.Authors.Add(new SyndicationPerson(null, author.AuthorName, null));
        }
        
        foreach (var tag in Tags)
        {
            feed.Categories.Add(new SyndicationCategory(tag.Tag));
        }

        var items = new List<SyndicationItem>();
        
        foreach (var chapter in chapters)
        {
            var item = new SyndicationItem();
            item.Title = new TextSyndicationContent(chapter.Title);
            item.Links.Add(new SyndicationLink(new Uri(chapter.Url)));
            item.Id = chapter.ChapterId;
            
            items.Add(item);
        }

        feed.Items = items;
        
        string atomFeed;
        using (var stringWriter = new StringWriter())
        {
            using (var xmlWriter = XmlWriter.Create(stringWriter, new XmlWriterSettings { Indent = true }))
            {
                var atomFormatter = new Atom10FeedFormatter(feed);
                atomFormatter.WriteTo(xmlWriter);
            }
            atomFeed = stringWriter.ToString();
        }
        
        return atomFeed;
    }
    //TODO onchanges create job to update metadata files in archives, etc.
}