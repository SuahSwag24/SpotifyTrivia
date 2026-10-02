import os
import asyncio
import shutil
import subprocess
import tempfile
import zipfile
from pathlib import Path

import httpx
from typing import Annotated
from fastapi import FastAPI, Header, HTTPException
from fastapi.responses import FileResponse
from pydantic import BaseModel

from dotenv import load_dotenv
load_dotenv()

API_KEY = os.environ["STEM_WORKER_API_KEY"]
MAX_DOWNLOAD_BYTES = 3 * 1024 * 1024 #  Cap maximum download size to 3 MB

app = FastAPI()

class SeparationRequest(BaseModel):
    previewUrl: str
    startSec: int = 5
    durationSec: int = 15

#   Functions
def check_key(x_api_key: str):
    if x_api_key != API_KEY:
        raise HTTPException(status_code = 401, detail="bad api key") #  Unauthorized API

def run(cmd: list[str], cwd: Path | None = None):
    result = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True)
    if result.returncode != 0:
        raise HTTPException(status_code=500, detail=f"cmd failed: {' '.join(cmd)}\n{result.stderr[-2000:]}") #  Internal server error

#   Silence check
def rms_is_silent(wav_path: Path, threshold_db: float = -50.0) -> bool:
    result = subprocess.run(
        ["ffmpeg", "-i", str(wav_path), "-af", "volumedetect", "-f", "null", "-"],
        capture_output=True, text=True,
    )

    for line in result.stderr.splitlines():
        if "mean_volume" in line:
            try:
                db = float(line.split(":")[1].strip().replace(" dB", ""))
                return db < threshold_db
            except:
                pass
    return False

async def fetch_preview(preview_url: str, raw_path: Path, max_attempts: int = 3):
    for attempt in range(1, max_attempts + 1):
        try:
            timeout = httpx.Timeout(10.0, connect=5.0)
            async with httpx.AsyncClient(timeout=timeout) as client:
                async with client.stream("GET", preview_url) as resp:
                    if resp.status_code != 200:
                        raise HTTPException(status_code=502, detail=f"failed to fetch previewUrl (status {resp.status_code})")

                    total = 0
                    with open(raw_path, "wb") as f:
                        async for chunk in resp.aiter_bytes():
                            total += len(chunk)
                            if total > MAX_DOWNLOAD_BYTES:
                                raise HTTPException(status_code=413, detail="preview too large")
                            f.write(chunk)
            return  # success

        except httpx.ConnectTimeout as exc:
            last_exc = exc
            print(f"[fetch_preview] attempt {attempt}/{max_attempts} timed out: {exc}")
            if attempt < max_attempts:
                await asyncio.sleep(1.5 * attempt)  # 1.5s, then 3s
                continue

    raise HTTPException(status_code=502, detail=f"preview fetch timed out after {max_attempts} attempts") from last_exc

#   Request
@app.post("/separate")
async def separate(req: SeparationRequest, x_api_key: Annotated[str, Header()]):
    check_key(x_api_key)
    print("DEBUG previewUrl:", repr(req.previewUrl))

    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        raw_path = d / "in.mp3"
        clip_path = d / "clip.wav"

        #   Step 1: Download the preview and make sure it is within capped size
        await fetch_preview(req.previewUrl, raw_path)

        #   Step 2: Trim audio to the requested window
        run([
            "ffmpeg", "-y", "-ss", str(req.startSec), "-t", str(req.durationSec),
            "-i", str(raw_path), str(clip_path),
        ])

        #   Step 3: Separate audio stems
        out_dir = d / "out"
        run([
            "python3", "-m", "demucs", "-n", "htdemucs", "--overlap", "0.1",
            "-o", str(out_dir), str(clip_path),
        ])

        stem_dir = out_dir / "htdemucs" / "clip"
        if not stem_dir.exists():
            raise HTTPException(status_code=500, detail="demucs did not produce any output") #  Demucs failed

        #   Step 4: Encode each stem to mp3 and flag out stems that are "almost" silent
        stems = [
            "drums",
            "bass",
            "other",
            "vocals"
        ]
        silent = []

        zip_path = d / "stems.zip"
        with zipfile.ZipFile(zip_path, "w") as zf:
            for stem in stems:
                wav_path = stem_dir / f"{stem}.wav"
                mp3_path = d / f"{stem}.mp3"

                run(["ffmpeg", "-y", "-i", str(wav_path), "-c:a", "libmp3lame", "-b:a", "96k", str(mp3_path)])

                #   Silent check call
                if rms_is_silent(wav_path):
                    silent.append(stem)
                zf.write(mp3_path, arcname=f"{stem}.mp3")

            manifest = {
                "stems" : stems,
                "silent" : silent,
                "durationMs" : req.durationSec * 1000,
            }
            zf.writestr("manifest.json", __import__("json").dumps(manifest))

        final_path = Path(tempfile.gettempdir()) / f"stems_{os.getpid()}_{id(req)}.zip"
        shutil.copy(zip_path, final_path)

    return FileResponse(final_path, media_type="application/zip", filename="stems.zip", background=None) 