"""Marks every frame of an .ico file with a purple ring, in place.

Used by apply_patch.py so the patched G-Helper's exe, window and tray icons are easy to tell apart
from the official app. The original icon is shrunk a little and a thick purple ring is drawn around it,
with a transparent gap between the two. Unlike a corner dot, the ring stays obvious at 16 px in the tray
and on light or dark taskbars, and works for every tray icon colour (blue, green, red, white, black).
Needs Pillow.
"""
import io
import pathlib
import struct
import sys
from PIL import Image, ImageDraw

RING = (176, 72, 255, 255)    # purple, not used by any of G-Helper's own icons
INNER = 0.74                  # the original icon's size inside the ring
RING_WIDTH = 0.095            # ring thickness, as a share of the icon size


def badge(frame):
    s = frame.size[0]
    big = 4
    S = s * big
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(out)
    d.ellipse((0, 0, S - 1, S - 1), fill=RING)
    w = S * RING_WIDTH
    d.ellipse((w, w, S - 1 - w, S - 1 - w), fill=(0, 0, 0, 0))
    inner = frame.convert("RGBA").resize((round(S * INNER), round(S * INNER)), Image.LANCZOS)
    o = (S - inner.size[0]) // 2
    out.alpha_composite(inner, (o, o))
    return out.resize((s, s), Image.LANCZOS)

def _dib(im, header=None):
    """32-bit BGRA DIB as stored in .ico files (bottom-up, height doubled, 1-bit AND mask).

    header: the original frame's 40-byte BITMAPINFOHEADER, reused byte for byte so the only change
    is the pixels (some Windows icon paths are stricter than others about fields like biSizeImage)."""
    w, h = im.size
    px = im.load()
    if header is None:
        header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, w * h * 4, 0, 0, 0, 0)
    out = bytearray(header)
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


def write_ico(path, frames, png_flags, headers=None):
    """Same frame order and same encoding per frame (PNG or 32-bit DIB) as the original file."""
    blobs = []
    headers = headers or [None] * len(frames)
    for im, is_png, hdr in zip(frames, png_flags, headers):
        if is_png:
            buf = io.BytesIO()
            im.save(buf, format="PNG")
            blobs.append(buf.getvalue())
        else:
            blobs.append(_dib(im, hdr))
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
            hdr = struct.unpack_from("<IiiHHI", data, off)
            if hdr != (40, w, h * 2, 1, 32, 0):
                raise SystemExit(f"{path}: bad DIB header {hdr} for {w}px")
            if size != 40 + w * h * 4 + ((w + 31) // 32) * 4 * h:
                raise SystemExit(f"{path}: wrong DIB size for {w}px")


# Small sizes Windows asks for (taskbar, Alt+Tab, Explorer lists). G-Helper's favicon.ico only has
# 64/128/256, so the shell has to shrink one; these are added (badged, from the largest frame) instead.
SMALL_SIZES = (48, 32, 24, 16)


def badge_file(path, add_small=False):
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
    headers = [None if png else data[off:off + 40] for _w, _h, png, off, _s in entries]
    flags = [e[2] for e in entries]
    expected = [(w, h, png) for w, h, png, _o, _s in entries]
    if add_small:
        have = {w for w, _h, _p, _o, _s in entries}
        big = frames[max(range(len(frames)), key=lambda i: frames[i].size[0])]
        for sz in SMALL_SIZES:
            if sz in have:
                continue
            frames.append(big.resize((sz, sz), Image.LANCZOS))
            flags.append(False)
            headers.append(None)
            expected.append((sz, sz, False))
    write_ico(path, frames, flags, headers)
    _check(path, expected)
    print(f"badged {pathlib.Path(path).name}: {[(w, 'png' if p else 'bmp') for w, h, p in expected]}")


if __name__ == "__main__":
    for p in sys.argv[1:]:
        badge_file(p, add_small=pathlib.Path(p).name.lower() == "favicon.ico")
