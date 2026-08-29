# SpotifyTrivia

**Managed and Developed by:** Suah Li Jea Richie (SuahSwag24)

**SpotifyTrivia** is a multiplayer trivia web game built with ASP.NET Core. Songs are fetched from player's Spotify playlists and the tracks retrieved will be used to create trivia rounds against others in a lobby.

## Overview

The app includes the following features:

- Spotify authentication and playlist access
- Trivia questions generation based on selected playlist
- Single-player trivia
- Multiplayer lobbies and games

## Tech Stack

- ASP.NET Core MVC for streamlined code maintenance and feature additions
- .NET 10
- SignalR for real-time multiplayer interaction
- Spotify Web API
- Deezer preview API for track previews
- Session-based app state


## Prerequisites

Before running the project, make sure you have:

- .NET 10 SDK installed
- A Spotify developer account
- A Spotify app created in the Spotify Developer Dashboard
- A local URL configured for the OAuth redirect

Note that **Spotify Premium is not required**.

## Spotify Setup

1. Go to the Spotify Developer Dashboard.
2. Create a new app.
3. Copy the Client ID and Client Secret.
4. Add a redirect URI matching your local app URL, for example:

```text
http://127.0.0.1:8080/callback
```

5. Save the values in your local configuration.

## Configuration

The app reads Spotify settings from configuration. You can set them using either:

- appsettings.Development.json
- user secrets
- environment variables

Example configuration:

```json
{
  "Spotify": {
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret",
    "RedirectUri": "http://127.0.0.1:8080/callback"
  }
}
```

User secrets option:

```bash
dotnet user-secrets set "Spotify:ClientId" "your-client-id"
dotnet user-secrets set "Spotify:ClientSecret" "your-client-secret"
dotnet user-secrets set "Spotify:RedirectUri" "http://127.0.0.1:8080/callback"
```

## Running the App

From the project root:

```bash
dotnet restore
dotnet run
```

The app is configured to run at:

```text
http://127.0.0.1:8080
```

## Current Status

This project already includes the core functionality for:

- playlist-based trivia
- lobby creation and joining
- real-time multiplayer rounds
- host-driven session flow
- leave/disconnect handling
- basic scoring and end-of-game flow

The remaining work is mainly in the next-phase area: analytics, UX polish, error clarity, and additional gameplay variations.

## Future Ideas

Potential next-phase enhancements:

- Additional game modes (TBD)
- Stat tracking
- Persistent storage (Low priority)

## License

This project is licensed under the MIT License. See the LICENSE file in the repository for details.
