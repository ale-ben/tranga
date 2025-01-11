using System.ComponentModel.DataAnnotations;
using System.Xml.Linq;
using API.Schema.Jobs;
using Microsoft.EntityFrameworkCore;

namespace API.Schema;

[PrimaryKey("ChapterId")]
public class Chapter : IComparable<Chapter>
{
    [MaxLength(64)]
    public string ChapterId { get; init; } = TokenGen.CreateToken(typeof(Chapter), 64);
    public int? VolumeNumber { get; private set; }
    [MaxLength(10)]
    public string ChapterNumber { get; private set; }
    public string Url { get; internal set; }
    public string? Title { get; private set; }
    public string ArchiveFileName { get; private set; }
    public bool Downloaded { get; internal set; } = false;
    
    public string ParentMangaId { get; internal set; }
    public Manga? ParentManga { get; init; }

    public Chapter(Manga parentManga, string url, string chapterNumberStruct, int? volumeNumber = null, string? title = null)
        : this(parentManga.MangaId, url, chapterNumberStruct, volumeNumber, title)
    {
        this.ParentManga = parentManga;
        this.ArchiveFileName = BuildArchiveFileName();
    }
    
    public Chapter(string parentMangaId, string url, string chapterNumber,
        int? volumeNumber = null, string? title = null)
    {
        this.ParentMangaId = parentMangaId;
        this.Url = url;
        this.ChapterNumber = chapterNumber;
        this.VolumeNumber = volumeNumber;
        this.Title = title;
    }

    public MoveFileOrFolderJob? UpdateChapterNumber(string chapterNumber)
    {
        this.ChapterNumber = chapterNumber;
        return UpdateArchiveFileName();
    }

    public MoveFileOrFolderJob? UpdateVolumeNumber(int? volumeNumber)
    {
        this.VolumeNumber = volumeNumber;
        return UpdateArchiveFileName();
    }

    public MoveFileOrFolderJob? UpdateTitle(string? title)
    {
        this.Title = title;
        return UpdateArchiveFileName();
    }

    private string BuildArchiveFileName()
    {
        return $"{this.ParentManga.Name} - Vol.{this.VolumeNumber ?? 0} Ch.{this.ChapterNumber}{(this.Title is null ? "" : $" - {this.Title}")}.cbz";
    }

    private MoveFileOrFolderJob? UpdateArchiveFileName()
    {
        string oldPath = GetArchiveFilePath();
        this.ArchiveFileName = BuildArchiveFileName();
        if (Downloaded)
        {
            return new MoveFileOrFolderJob(oldPath, GetArchiveFilePath());
        }
        return null;
    }
    
    /// <summary>
    /// Creates full file path of chapter-archive
    /// </summary>
    /// <returns>Filepath</returns>
    internal string GetArchiveFilePath()
    {
        return Path.Join(TrangaSettings.downloadLocation, ParentManga.FolderName, ArchiveFileName);
    }

    public bool IsDownloaded()
    {
        string path = GetArchiveFilePath();
        return File.Exists(path);
    }

    private static int CompareChapterNumbers(string ch1, string ch2)
    {
        var ch1Arr = ch1.Split('.').Select(c => int.Parse(c)).ToArray();
        var ch2Arr = ch2.Split('.').Select(c => int.Parse(c)).ToArray();

        int i = 0, j = 0;
        
        while (i < ch1Arr.Length && j < ch2Arr.Length)
        {
            if (ch1Arr[i] < ch2Arr[j])
                return -1;
            if (ch1Arr[i] > ch2Arr[j])
                return 1;
            i++;
            j++;
        }
        
        return 0;
    }

    public int CompareTo(Chapter? other)
    {
        if(other is not { } otherChapter)
            throw new ArgumentException($"{other} can not be compared to {this}");
        return this.VolumeNumber?.CompareTo(otherChapter.VolumeNumber) switch
        {
            <0 => -1,
            >0 => 1,
            _ => CompareChapterNumbers(this.ChapterNumber, otherChapter.ChapterNumber)
        };
    }
    
    internal string GetComicInfoXmlString()
    {
        XElement comicInfo = new XElement("ComicInfo",
            new XElement("Tags", string.Join(',', ParentManga.Tags.Select(tag => tag.Tag))),
            new XElement("LanguageISO", ParentManga.OriginalLanguage),
            new XElement("Title", this.Title),
            new XElement("Writer", string.Join(',', ParentManga.Authors.Select(author => author.AuthorName))),
            new XElement("Volume", this.VolumeNumber),
            new XElement("Number", this.ChapterNumber)
        );
        return comicInfo.ToString();
    }
}