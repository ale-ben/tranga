﻿using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tranga.Connectors;

public class MangaDex : Connector
{
    public override string name { get; }
    private DownloadClient _downloadClient = new ();

    public MangaDex()
    {
        name = "MangaDex.org";
    }

    public override Publication[] GetPublications()
    {
        const int limit = 100;
        string publicationsUrl = $"https://api.mangadex.org/manga?limit={limit}&offset=";
        int offset = 0;
        int total = int.MaxValue;
        HashSet<Publication> publications = new();
        while (offset < total)
        {
            DownloadClient.RequestResult requestResult = _downloadClient.GetPage(string.Concat(publicationsUrl, "0"));
            JsonObject? result = JsonSerializer.Deserialize<JsonObject>(requestResult.result);
            if (result is null)
                break;
            
            total = result["total"]!.GetValue<int>();
            JsonArray mangaInResult = result["data"]!.AsArray();
            foreach (JsonObject manga in mangaInResult)
            {
                JsonObject attributes = manga["attributes"].AsObject();
                
                string title = attributes["title"]!.AsObject().ContainsKey("en") && attributes["title"]!["en"] is not null
                    ? attributes["title"]!["en"]!.GetValue<string>()
                    : "";
                
                string? description = attributes["description"]!.AsObject().ContainsKey("en") && attributes["description"]!["en"] is not null
                    ? attributes["description"]!["en"]!.GetValue<string?>()
                    : null;

                JsonArray altTitlesObject = attributes["altTitles"]!.AsArray();
                string[,] altTitles = new string[altTitlesObject.Count, 2];
                int titleIndex = 0;
                foreach (JsonObject altTitleObject in altTitlesObject)
                {
                    string key = ((IDictionary<string, JsonNode?>)altTitleObject!).Keys.ToArray()[0];
                    altTitles[titleIndex, 0] = key;
                    altTitles[titleIndex++, 1] = altTitleObject[key]!.GetValue<string>();
                }

                JsonArray tagsObject = attributes["tags"]!.AsArray();
                HashSet<string> tags = new();
                foreach (JsonObject tagObject in tagsObject)
                {
                    if(tagObject!["attributes"]!["name"]!.AsObject().ContainsKey("en"))
                        tags.Add(tagObject!["attributes"]!["name"]!["en"]!.GetValue<string>());
                }
                
                JsonArray relationships = manga["relationships"]!.AsArray();
                string poster = relationships.FirstOrDefault(relationship => relationship["type"].GetValue<string>() == "cover_art")["id"].GetValue<string>();

                JsonObject linksObject = attributes["links"]!.AsObject();
                string[,] links = new string[linksObject.Count, 2];
                int linkIndex = 0;
                foreach (string key in ((IDictionary<string, JsonNode?>)linksObject).Keys)
                {
                    links[linkIndex, 0] = key;
                    links[linkIndex++, 1] = linksObject[key]!.GetValue<string>();
                }
                
                int? year = attributes.ContainsKey("year") && attributes["year"] is not null
                    ? attributes["year"]!.GetValue<int?>()
                    : null;

                string? originalLanguage = attributes.ContainsKey("originalLanguage") && attributes["originalLanguage"] is not null
                    ? attributes["originalLanguage"]!.GetValue<string?>()
                    : null;
                
                string status = attributes["status"]!.GetValue<string>();

                Publication pub = new Publication(
                    title,
                    description,
                    altTitles,
                    tags.ToArray(),
                    poster,
                    links,
                    year,
                    originalLanguage,
                    status,
                    this,
                    manga["id"]!.GetValue<string>()
                );
                publications.Add(pub);
            }
            offset += limit;
        }

        return publications.ToArray();
    }

    public override Chapter[] GetChapters(Publication publication)
    {
        throw new NotImplementedException();
    }

    public override void DownloadChapter(Chapter chapter)
    {
        throw new NotImplementedException();
    }
}