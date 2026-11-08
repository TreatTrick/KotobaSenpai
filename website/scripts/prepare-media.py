"""Optimize the project's existing demonstrations for the product website."""

from pathlib import Path
import subprocess

from PIL import Image
import imageio_ffmpeg


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "docs" / "assets"
DESTINATION = ROOT / "website" / "static" / "media"
DESTINATION.mkdir(parents=True, exist_ok=True)
FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()

CLIPS = {
    "hero-en": "overlay-reading-demo.gif",
    "hero-zh": "overlay-reading-demo-cn.gif",
    "furigana": "furigana-appears.gif",
    "analysis-en": "contextual-analysis-en.gif",
    "analysis-zh": "contextual-analysis-cn.gif",
    "setup-en": "agent-assisted-setup-en.gif",
    "setup-zh": "agent-assisted-setup-cn.gif",
    "onboarding-en": "first-run-onboarding-en.gif",
    "onboarding-zh": "first-run-onboarding-cn.gif",
}

for name, filename in CLIPS.items():
    original = SOURCE / filename
    subprocess.run([
        FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-i", str(original),
        "-vf", "fps=24,scale='trunc(min(1600,iw)/2)*2':-2:flags=lanczos", "-an",
        "-c:v", "libx264", "-preset", "slow", "-crf", "22",
        "-pix_fmt", "yuv420p", "-movflags", "+faststart", str(DESTINATION / f"{name}.mp4"),
    ], check=True)
    with Image.open(original) as animation:
        animation.seek(min(animation.n_frames - 1, max(0, animation.n_frames // 2)))
        poster = animation.convert("RGB")
        poster.thumbnail((1600, 1200))
        poster.save(DESTINATION / f"{name}.webp", quality=86, method=6)
    print(f"Prepared {name}", flush=True)

for language in ["en", "cn", "ko", "es", "pt", "ru"]:
    with Image.open(SOURCE / f"multi-language-{language}.png") as original:
        screenshot = original.convert("RGB")
        screenshot.thumbnail((1600, 1200))
        screenshot.save(DESTINATION / f"language-{language}.webp", quality=86, method=6)

with Image.open(ROOT / "src/KotobaSenpai.App/Assets/Branding/app-icon.png") as original:
    for size, filename in [(96, "icon.webp"), (512, "social-icon.webp")]:
        icon = original.convert("RGBA")
        icon.thumbnail((size, size))
        icon.save(DESTINATION / filename, quality=90, method=6)

print("Website media is ready.")
