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
        [property: JsonPropertyName("artist")] DeezerArtistDto? Artist
    );

    internal record DeezerArtistDto(
        [property: JsonPropertyName("name")] string Name
    );
}
