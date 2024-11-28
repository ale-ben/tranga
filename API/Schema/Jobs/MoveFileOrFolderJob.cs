﻿namespace API.Schema.Jobs;

public class MoveFileOrFolderJob(string fromLocation, string toLocation, string? parentJobId = null, string[]? dependsOnJobIds = null)
    : Job(TokenGen.CreateToken(typeof(MoveFileOrFolderJob), 64), JobType.MoveFileJob, TimeSpan.Zero, parentJobId, dependsOnJobIds)
{
    public string FromLocation { get; init; } = fromLocation;
    public string ToLocation { get; init; } = toLocation;
}