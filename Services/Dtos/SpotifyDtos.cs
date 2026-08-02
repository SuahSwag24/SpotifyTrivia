using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization;

namespace SpotifyTrivia.Services.Dtos
{
    //  Playlist DTOs
    internal record SpotifyPlaylistsResponse(
        [property: JsonPropertyName("items")] List<SpotifyPlaylistDto>? Items
    );

    internal record SpotifyPlaylistDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("images")] List<SpotifyImageDto>? Images
    );

    //  Playlist Tracks DTOs
    internal record SpotifyPlaylistTracksResponse(
        [property: JsonPropertyName("items")] List<SpotifyPlaylistTracksDto>? Items
    );

    internal record SpotifyPlaylistTracksDto(
        [property: JsonPropertyName("track")] SpotifyTrackDto? Track
    );

    internal record SpotifyTrackDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("artists")] List<SpotifyArtistDto>? Artists,
        [property: JsonPropertyName("album")] SpotifyAlbumDto? Album,
        [property: JsonPropertyName("preview_url")] string? PreviewUrl
    );

    //  Shared DTOs
    internal record SpotifyImageDto(
        [property: JsonPropertyName("url")] string Url
    );

    internal record SpotifyArtistDto(
        [property: JsonPropertyName("name")] string Name
    );

    internal record SpotifyAlbumDto(
        [property: JsonPropertyName("images")] List<SpotifyImageDto>? Images
    );

    //  User Profile DTOs
    internal record SpotifyUserProfileDto(
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("images")] List<SpotifyImageDto>? Images,
        [property: JsonPropertyName("product")] string? Product,
        [property: JsonPropertyName("external_urls")] SpotifyExternalUrlsDto? ExternalUrls
    );

    internal record SpotifyExternalUrlsDto(
        [property: JsonPropertyName("spotify")] string? Spotify
    );
}
