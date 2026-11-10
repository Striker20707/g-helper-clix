"""Adds a small pink dot (bottom right) to every frame of an .ico file, in place.

Used by apply_patch.py so the patched G-Helper's exe, window and tray icons are easy to tell apart
from the official app. Needs Pillow.
"""
import io
import pathlib
import struct
import sys
from PIL import Image, ImageDraw

BADGE = (236, 72, 153, 255)   # pink, not used by G-Helper's eco/standard/ultimate icons
RING = (20, 20, 20, 255)

def badge(frame):
    s = frame.size[0]
    big = 4
    im = frame.convert("RGBA").resize((s * big, s * big), Image.LANCZOS)
    d = ImageDraw.Draw(im)
    S = s * big
    r = S * 0.16                      # badge radius
    cx, cy = S - r - S * 0.01, S - r - S * 0.01
    ring = max(big, S * 0.035)
    d.ellipse((cx - r - ring, cy - r - ring, cx + r + ring, cy + r + ring), fill=RING)
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=BADGE)
    return im.resize((s, s), Image.LANCZOS)

def _dib(im):
    """32-bit BGRA DIB as stored in .ico files (bottom-up, height doubled, 1-bit AND mask)."""
    w, h = im.size
    px = im.load()
    out = bytearray(struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0))
    for y in range(h - 1, -1, -1):
        for x in range(w):
            r, g, b, a = px[x, y]
            out += bytes((b, g, r, a))
    stride = ((w + 31) // 32) * 4
    for y in range(h - 1, -1, -1):
        row = bytearray(stride)
        for x in range(w):
            if px[x, y][3] == 0:
                row[x // 8] |= 0x80 >> (x % 8)
        out += row
    return bytes(out)


def write_ico(path, frames):
    """Like the originals: uncompressed 32-bit frames, PNG only for 256 px."""
    blobs = []
    for im in frames:
        if im.size[0] >= 256:
            buf = io.BytesIO()
            im.save(buf, format="PNG")
            blobs.append(buf.getvalue())
        else:
            blobs.append(_dib(im))
    head = struct.pack("<HHH", 0, 1, len(frames))
    offset = 6 + 16 * len(frames)
    entries = b""
    for im, blob in zip(frames, blobs):
        w, h = im.size
        entries += struct.pack("<BBBBHHII", w % 256, h % 256, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
    with open(path, "wb") as f:
        f.write(head + entries + b"".join(blobs))


def badge_file(path):
    ico = Image.open(path)
    sizes = list(ico.info["sizes"])
    frames = []
    for sz in sizes:
        ico.size = sz
        frames.append(badge(ico.copy()).convert("RGBA"))
    write_ico(path, frames)
    print(f"badged {pathlib.Path(path).name}: {sorted(sizes)}")


if __name__ == "__main__":
    for p in sys.argv[1:]:
        badge_file(p)
