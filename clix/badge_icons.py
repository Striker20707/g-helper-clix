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


def _entries(data):
    """(width, height, is_png, offset, size) for each frame, in the file's own order."""
    if data[:4] != b"\0\0\1\0":
        raise SystemExit("not an .ico file")
    n = struct.unpack_from("<H", data, 4)[0]
    out = []
    for i in range(n):
        w, h, _c, _r, _p, _bpp, size, off = struct.unpack_from("<BBBBHHII", data, 6 + 16 * i)
        out.append((w or 256, h or 256, data[off:off + 4] == b"\x89PNG", off, size))
    return out


def write_ico(path, frames, png_flags):
    """Same frame order and same encoding per frame (PNG or 32-bit DIB) as the original file."""
    blobs = []
    for im, is_png in zip(frames, png_flags):
        if is_png:
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


def _check(path, expected):
    """Re-read the written file with a plain parser: same frames, valid headers, badge pixel present."""
    data = pathlib.Path(path).read_bytes()
    got = [(w, h, png) for w, h, png, _o, _s in _entries(data)]
    if got != expected:
        raise SystemExit(f"{path}: frames {got} != original {expected}")
    for w, h, png, off, size in _entries(data):
        if off + size > len(data):
            raise SystemExit(f"{path}: frame {w}px runs past end of file")
        if not png:
            hdr = struct.unpack_from("<IiiHH", data, off)
            if hdr != (40, w, h * 2, 1, 32):
                raise SystemExit(f"{path}: bad DIB header {hdr} for {w}px")
            if size != 40 + w * h * 4 + ((w + 31) // 32) * 4 * h:
                raise SystemExit(f"{path}: wrong DIB size for {w}px")


def badge_file(path):
    data = pathlib.Path(path).read_bytes()
    entries = _entries(data)
    frames = []
    for w, h, is_png, off, size in entries:
        sub = struct.pack("<HHH", 0, 1, 1) + struct.pack("<BBBBHHII", w % 256, h % 256, 0, 0, 1, 32, size, 22) + data[off:off + size]
        frame = Image.open(io.BytesIO(sub))
        frame.load()
        if frame.size != (w, h):
            raise SystemExit(f"{path}: frame {w}px decoded as {frame.size}")
        frames.append(badge(frame.convert("RGBA")).convert("RGBA"))
    write_ico(path, frames, [e[2] for e in entries])
    _check(path, [(w, h, png) for w, h, png, _o, _s in entries])
    print(f"badged {pathlib.Path(path).name}: {[(w, 'png' if p else 'bmp') for w, h, p, _o, _s in entries]}")


if __name__ == "__main__":
    for p in sys.argv[1:]:
        badge_file(p)
