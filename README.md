# SpotifyTrivia

**Managed and Developed by:** Suah Li Jea Richie (SuahSwag24)

**SpotifyTrivia** is a multiplayer trivia web game built with ASP.NET Core. Songs are fetched from player's Spotify playlists and the tracks retrieved will be used to create trivia rounds against others in a lobby.

Project is deployed in https://spotifytrivia.onrender.com using Render. However, to retrieve account information, email is required in the Web API Developer Dashboard User Management settings to allow permitted access to the web game.

## Overview

The app includes the following features:

- Spotify authentication and playlist access
- Trivia questions generation based on selected playlist
- Single-player trivia
- Multiplayer lobbies and games
- Multiple game modes (Classic Guess Song, Guess Artist, and Stem Guess)
- Audio stem separation worker (FastAPI + Demucs) for isolated instrument trivia rounds

### Stem Guess Audio Pipeline

When a lobby selects the **Stem Guess** mode:
1. Deezer preview URLs are fetched for fair-distributed playlist tracks via ISRC.
2. Separation jobs (`StemJob`) are queued into an asynchronous in-memory channel (`StemPipeline`).
3. An ASP.NET Core background hosted service calls the FastAPI stem worker (`POST /separate`).
4. The worker trims the audio, separates 4 instrument layers (`drums`, `bass`, `other`, `vocals`) via Demucs (`htdemucs`), filters silent stems using FFmpeg `volumedetect`, and packages MP3 stems with `manifest.json`.
5. Stems are cached in-memory (`StemStore`) and streamed to players via `GET /api/stems/{jobId}/{stem}`.

## Tech Stack

- ASP.NET Core MVC for streamlined code maintenance and feature additions
- .NET 10
- SignalR for real-time multiplayer interaction
- Background hosted services & `System.Threading.Channels` for asynchronous audio processing pipelines
- Spotify Web API
- Deezer preview API for track previews
- Python 3 & FastAPI for the stem separation microservice worker
- Demucs (`htdemucs`) & FFmpeg for audio processing, trimming, and stem separation
- Session-based app state


## Prerequisites

Before running the project, make sure you have:

- .NET 10 SDK installed
- A Spotify developer account
- A Spotify app created in the Spotify Developer Dashboard
- A local URL configured for the OAuth redirect
- (Optional / For Stem Mode) Python 3.10+ and FFmpeg installed and available in your system PATH
  - *Performance note:* Demucs (`htdemucs`) runs AI stem separation. A CUDA-supported GPU is recommended for faster separation; CPU processing takes ~5–15 seconds per 15-second snippet.

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

Store the Spotify credentials and Stem Worker settings as .NET user secrets. Do not commit credentials to `appsettings.json`, `appsettings.Development.json`, or any other tracked file.

From the project root, initialize user secrets if needed and set the values:

```bash
dotnet user-secrets init
dotnet user-secrets set "Spotify:ClientId" "your-client-id"
dotnet user-secrets set "Spotify:ClientSecret" "your-client-secret"
dotnet user-secrets set "Spotify:RedirectUri" "http://127.0.0.1:8080/callback"
dotnet user-secrets set "StemWorker:BaseUrl" "http://127.0.0.1:8000"
dotnet user-secrets set "StemWorker:ApiKey" "your-stem-worker-api-key"
```

The project is configured with a `UserSecretsId`, so ASP.NET Core loads these values automatically when running in the Development environment. User secrets are stored outside the repository on your machine.

### Stem Worker Configuration

The Python stem separation worker requires an API key configured via environment variables. In the `stem-worker/` directory:

1. Create a `.env` file:
   ```env
   STEM_WORKER_API_KEY=your-stem-worker-api-key
   ```
2. Make sure this matches the `StemWorker:ApiKey` configured in the .NET user secrets (keep `"ApiKey": ""` empty in `appsettings.json`).

## Executing the App

### 1. (Optional) Run the Stem Separation Worker

If using the Stem Guess game mode:

```bash
cd stem-worker
python -m venv .venv
# Activate the virtual environment:
# Windows (PowerShell): .venv\Scripts\Activate.ps1
# Linux/macOS: source .venv/bin/activate
pip install -r requirements.txt
uvicorn main:app --host 127.0.0.1 --port 8000
```

### 2. Run the ASP.NET Core App

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
- multiple game modes:
  - Classic Guess Song
  - Guess Artist
  - Stem Guess (using the Python Demucs worker for instrument layer separation, background queue pipeline, in-memory caching, and stem audio streaming API)

The remaining work is mainly in the next-phase area: analytics, UX polish, error clarity, and additional gameplay variations.

## Future Ideas

Potential next-phase enhancements:

- Additional game modes (TBD)
- Stat tracking
- Persistent storage (Low priority)

## License

This project is licensed under the MIT License. See the LICENSE file in the repository for details.
