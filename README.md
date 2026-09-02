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

## Development Tools

An IDE is not required to run this project. The .NET SDK and command line are sufficient.

You may use any of the following development environments:

- Visual Studio 2022 with an updated version that supports .NET 10
- Visual Studio Code with the C# Dev Kit extension
- Any editor with the .NET 10 SDK installed

## Spotify Setup

1. Go to the Spotify Developer Dashboard.
2. Create a new app.
3. Copy the Client ID and Client Secret.
4. Add a redirect URI matching the URL configured in `Properties/launchSettings.json`, for example:

```text
http://127.0.0.1:8080/callback
```

5. Save the redirect URI in Spotify's app settings.

## Configuration

Store the Spotify Client ID and Client Secret as .NET user secrets. Do not commit credentials to `appsettings.json`, `appsettings.Development.json`, or any other tracked file.

From the project root, initialize user secrets if needed and set the Spotify values:

```bash
dotnet user-secrets init
dotnet user-secrets set "Spotify:ClientId" "your-client-id"
dotnet user-secrets set "Spotify:ClientSecret" "your-client-secret"
dotnet user-secrets set "Spotify:RedirectUri" "http://127.0.0.1:8080/callback"
```

The project is configured with a `UserSecretsId`, so ASP.NET Core loads these values automatically when running in the Development environment. User secrets are stored outside the repository on your machine.

## Executing the App

The launch profile in `Properties/launchSettings.json` sets the Development environment and binds the app to:

```text
http://127.0.0.1:8080
```

The Spotify redirect URI must use the same host and port with `/callback` appended.

From the project root, restore dependencies and run the application:

```bash
dotnet restore
dotnet run
```

Open the application at:

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
