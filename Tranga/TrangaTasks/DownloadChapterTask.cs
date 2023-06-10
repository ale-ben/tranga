﻿using Logging;
using Newtonsoft.Json;

namespace Tranga.TrangaTasks;

public class DownloadChapterTask : TrangaTask
{
    public string connectorName { get; }
    public Publication publication { get; }
    public string language { get; }
    public Chapter chapter { get; }
    [JsonIgnore]public DownloadNewChaptersTask? parentTask { get; init; }
    
    public DownloadChapterTask(Task task, string connectorName, Publication publication, Chapter chapter, string language = "en", DownloadNewChaptersTask? parentTask = null) : base(task, TimeSpan.Zero)
    {
        this.chapter = chapter;
        this.connectorName = connectorName;
        this.publication = publication;
        this.language = language;
        this.parentTask = parentTask;
    }

    protected override void ExecuteTask(TaskManager taskManager, Logger? logger, CancellationToken? cancellationToken = null)
    {
        if (cancellationToken?.IsCancellationRequested??false)
            return;
        Connector connector = taskManager.GetConnector(this.connectorName);
        connector.DownloadChapter(this.publication, this.chapter, this, cancellationToken);
        taskManager.DeleteTask(this);
    }
    
    public new float IncrementProgress(float amount)
    {
        this.progress += amount;
        this.lastChange = DateTime.Now;
        parentTask?.IncrementProgress(amount);
        return this.progress;
    }

    public override string ToString()
    {
        return $"{base.ToString()}, {connectorName}, {publication.sortName} {publication.internalId}, Vol.{chapter.volumeNumber} Ch.{chapter.chapterNumber}";
    }
}