using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace SpotifyTrivia.Services.Dtos
{
    internal record DeezerSearchResponse(
        [property: JsonPropertyName("data")] List<DeezerTrackDto>? Data
    );

    internal record DeezerTrackDto(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("preview")] string? Preview,
        [property: JsonPropertyName("artist")] DeezerArtistDto? Artist,
        [property: JsonPropertyName("album")] DeezerAlbumDto? Album = null
    );

    public record DeezerArtistDto(
        [property: JsonPropertyName ("id")] long Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("picture_medium")] string? PictureMedium = null
    );

    public record DeezerTrackResponse(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("preview")] string? Preview,
        [property: JsonPropertyName("artist")] DeezerArtistDto? Artist,
        [property: JsonPropertyName("isrc")] string? Isrc = null
    );

    internal record DeezerAlbumDto(
        [property: JsonPropertyName("cover_medium")] string? CoverMedium = null
    );

    internal record DeezerArtistListResponse(
        [property: JsonPropertyName("data")] List<DeezerArtistDto>? Data
    );

    public record SearchArtistResult(long ArtistId, string Name, string? PictureUrl);
    public record SearchTrackResult(long TrackId, string Title, long ArtistId, string ArtistName, string? AlbumCoverUrl);
}
