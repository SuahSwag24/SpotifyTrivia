# Stem Separation Worker

A lightweight FastAPI microservice that extracts audio stems (`drums`, `bass`, `other`, `vocals`) from preview tracks using [Demucs](https://github.com/facebookresearch/demucs) (`htdemucs`) and FFmpeg.

This worker is used by the **Stem Guess** game mode in the SpotifyTrivia ASP.NET Core application.

---

## Features

- Downloads track previews (e.g. from Deezer preview CDN).
- Trims audio to custom window offsets (`startSec`, `durationSec`) via FFmpeg.
- Runs AI stem separation via Demucs (`htdemucs` model).
- Detects silent/near-silent stems using FFmpeg `volumedetect` filter.
- Compresses stems into MP3 format (96k libmp3lame).
- Packages audio stems and a `manifest.json` into a single ZIP response.
- Protected by `X-Api-Key` authentication.

---

## Prerequisites

- **Python 3.10+**
- **FFmpeg**: Must be installed and accessible in your system `PATH`.
  - Windows: Install via winget (`winget install Gyan.FFmpeg`) or Chocolatey (`choco install ffmpeg`).
  - Linux/Ubuntu: `sudo apt install ffmpeg`
  - macOS: `brew install ffmpeg`

---

## Setup & Installation

1. Navigate to the worker directory:
   ```bash
   cd stem-worker
   ```

2. Create and activate a virtual environment:
   ```bash
   python -m venv .venv

   # Windows (PowerShell):
   .venv\Scripts\Activate.ps1

   # Linux / macOS:
   source .venv/bin/activate
   ```

3. Install required packages:
   ```bash
   pip install -r requirements.txt
   ```

4. Configure the environment variables:
   Create a `.env` file in `stem-worker/`:
   ```env
   STEM_WORKER_API_KEY=your-secure-api-key
   ```

---

## Running the Service

Start the FastAPI server using Uvicorn:

```bash
uvicorn main:app --host 127.0.0.1 --port 8000 --reload
```

---

## API Reference

### `POST /separate`

Separates a preview audio track into individual stems.

#### Headers
- `X-Api-Key`: Must match `STEM_WORKER_API_KEY`.
- `Content-Type`: `application/json`

#### Request Body
```json
{
  "previewUrl": "https://cdnt-preview.dzcdn.net/...",
  "startSec": 5,
  "durationSec": 15
}
```

#### Response
- Status: `200 OK`
- Media Type: `application/zip`
- Archive Content:
  - `drums.mp3`
  - `bass.mp3`
  - `other.mp3`
  - `vocals.mp3`
  - `manifest.json` (lists detected stems, silent stems, and duration in ms)

---

## Integration with SpotifyTrivia (.NET)

In the root .NET project, configure the worker address and API key via user secrets:

```bash
dotnet user-secrets set "StemWorker:BaseUrl" "http://127.0.0.1:8000"
dotnet user-secrets set "StemWorker:ApiKey" "your-secure-api-key"
```
