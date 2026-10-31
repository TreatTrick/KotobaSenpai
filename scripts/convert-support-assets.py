"""Export Clutch's full-size transparent HEIC sequences for WPF.

Development only: pip install Pillow pillow-heif. Normal builds use the checked-in PNGs.
"""

import argparse
import hashlib
import json
import shutil
from pathlib import Path

from PIL import Image
import pillow_heif


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("clutch", type=Path, help="Path to the Clutch repository")
    args = parser.parse_args()
    source = args.clutch / "Clutch"
    output = Path(__file__).resolve().parents[1] / "src/KotobaSenpai.App/Assets/Support"
    output.mkdir(parents=True, exist_ok=True)
    pillow_heif.register_heif_opener()
    metadata = {"fps": 24, "clips": {}}
    canvas = None
    for clip in ("A", "B"):
        original = source / "Mascot" / f"Mascot{clip}.heic"
        destination = output / clip
        destination.mkdir(exist_ok=True)
        with Image.open(original) as sequence:
            if canvas is not None and sequence.size != canvas:
                raise ValueError("Both clips must share the original canvas.")
            canvas = sequence.size
            for index in range(sequence.n_frames):
                sequence.seek(index)
                frame = sequence.convert("RGBA")
                if frame.size != canvas or frame.getchannel("A").getextrema()[0] != 0:
                    raise ValueError("Every frame must preserve the transparent canvas.")
                frame.save(destination / f"{index:03}.png", optimize=True)
            metadata["clips"][clip] = {
                "frames": sequence.n_frames,
                "width": canvas[0],
                "height": canvas[1],
                "sourceSha256": hashlib.sha256(original.read_bytes()).hexdigest(),
            }
            shutil.copyfile(destination / (f"{sequence.n_frames - 1:03}.png" if clip == "A" else "000.png"),
                            output / ("happy.png" if clip == "A" else "thanks.png"))
        print(f"Clip {clip}: {metadata['clips'][clip]}")
    for asset, filename in (("WeChatQRCode", "wechat.jpg"), ("AlipayQRCode", "alipay.jpg"),
                            ("PayPalQRCode", "paypal.png")):
        shutil.copyfile(source / "Assets.xcassets" / f"{asset}.imageset" / filename, output / filename)
    (output / "frames.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
