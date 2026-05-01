# GifToWebM

A simple C# console utility that converts short GIF, MP4, AVIF, or PNG animations (up to 3 seconds) into optimized WebM videos using FFmpeg.

## Features

- Extracts and scales frames from a GIF, MP4, AVIF, or PNG sequence
- For MP4: automatically detects and preserves the original FPS
- For AVIF: detects animated streams and auto-calculates FPS from metadata
- Preserves original image proportions without stretching
- Allows specifying output frame size in pixels (default: 512px for longest side)
- Optional padding to square canvas with transparent background
- Fast emoji mode (-e) for quick 100x100 conversions
- Adds optional smooth border with customizable color and size
- **Supports optional border blur with customizable radius**
- **Full AVIF support**: multi-stream detection, merges color and alpha channels for transparency
- Calculates correct FPS based on GIF timing, AVIF metadata, or uses original FPS for MP4
- Encodes frames into a VP9 WebM file with alpha channel support (GIF/PNG/AVIF only)
- Adjusts CRF until output size is under 256 KB (or 64 KB if emoji mode is enabled)
- **Input video (GIF/MP4/AVIF) is always trimmed to the first 3 seconds**

## Requirements

- .NET Framework 4.7.2 (WPF support required)
- `ffmpeg.exe` and `ffprobe.exe` must be present in the same directory as the executable
- **ffmpeg must be built with AVIF support (libaom/libdav1d) for AVIF input**

## Usage

```
Converter.exe [options]
```

### Options

| Option                | Description                                          |
|----------------------|------------------------------------------------------|
| `-i`, `--input`      | Input GIF, MP4, AVIF file, or PNG file (for PNG sequence)  |
| `-o`, `--output`     | Output WebM file (default)           |
| `-c`, `--crf-step`   | CRF step increment (default: `2`)                   |
| `-b`, `--border`     | Add border to frames (default: disabled)            |
| `--border-size`      | Border thickness in pixels (default: `2`)           |
| `--border-color`     | Border color hex (default: `#FFFFFF`)               |
| `--blur <value>`     | Border blur radius (integer, required value; default: no blur) |
| `--fps`, `--input-fps` | Input FPS for frame extraction (default: `10`). Auto-calculated for GIF/MP4/AVIF |
| `--target-fps`, `--output-fps` | Target output FPS for WebM (default: `30`, Telegram standard) |
| `-s`, `--size`       | Target size in pixels for longest side (default: `512`) |
| `-p`, `--pad`        | Add padding to square canvas (default: disabled)    |
| `-e`, `--emoji`      | Set target size to 100x100 for emoji output        |
| `-h`, `--help`       | Show help                                          |

## Examples

Convert GIF to WebM with default settings (preserving proportions, max size 512px):
```
Converter.exe -i animation.gif
```

Convert GIF to WebM with custom output filename:
```
Converter.exe -i animation.gif -o result.webm
```

Convert animated AVIF to WebM with transparency (output: animation.webm):
```
Converter.exe -i animation.avif
```

Convert AVIF to emoji WebM (output: sticker.webm):
```
Converter.exe -i sticker.avif -e
```

Convert GIF to WebM with padding to square canvas:
```
Converter.exe -i animation.gif --pad
```

Convert MP4 to WebM (preserving original FPS, only first 3 seconds, output: video.webm):
```
Converter.exe -i video.mp4
```

Convert GIF to 100x100 emoji (output: animation.webm):
```
Converter.exe -i animation.gif -e
```

Convert with custom size and white border:
```
Converter.exe -i animation.gif -s 256 -b
```

Convert with custom border color and size:
```
Converter.exe -i animation.gif -b --border-size 4 --border-color "#FF0000"
```

Convert with custom border blur:
```
Converter.exe -i animation.gif -b --blur 2
```

Convert PNG sequence with custom FPS:
```
Converter.exe -i "first_frame.png" --input-fps 24 --target-fps 30
```

Convert 60 FPS video to Telegram-compatible 30 FPS:
```
Converter.exe -i video60fps.mp4
```

Convert with custom output FPS (not recommended for Telegram):
```
Converter.exe -i animation.gif --target-fps 60
```

Show help:
```
Converter.exe --help
```

## FPS Handling

The converter now separates **input FPS** (for frame extraction) and **target FPS** (for output):

### Input FPS (`--fps` / `--input-fps`)
Controls how frames are extracted from the source file:
- **GIF**: Auto-calculated from frame delays in metadata
- **MP4**: Auto-detected using ffprobe
- **AVIF**: Auto-detected from stream metadata
- **PNG sequence**: Default 10 FPS, or user-specified
- Can be overridden with `--input-fps` parameter

### Target FPS (`--target-fps` / `--output-fps`)
Controls the output WebM framerate:
- **Default: 30 FPS** (Telegram standard)
- **Critical for iOS**: Telegram on iOS strictly requires 30 FPS
  - 60 FPS videos play 2x slower on iOS
  - Other framerates may cause playback issues
- FFmpeg automatically adjusts frames:
  - **Higher input FPS** (e.g., 60 → 30): Drops frames
  - **Lower input FPS** (e.g., 15 → 30): Duplicates frames
  - **Duration remains unchanged**

### Example FPS Conversion
```bash
# 60 FPS source → 30 FPS output (standard case)
Converter.exe -i video60fps.mp4
# Output: Input FPS: 60, Target output FPS: 30
# FFmpeg will drop every other frame

# Custom frame extraction with standard output
Converter.exe -i video.mp4 --input-fps 15 --target-fps 30
# Extracts 15 FPS, outputs 30 FPS (frames duplicated)
```

## AVIF Support Details

The converter fully supports animated AVIF files:

- **Multi-stream detection**: Automatically identifies which stream contains animation (AVIF can have multiple video streams)
- **Alpha channel support**: Merges separate color and alpha streams using `alphamerge` filter
- **Frame rate detection**: Automatically detects FPS from AVIF metadata
- **Duration limits**: Respects 3-second maximum duration limit
- **Static AVIF**: Single-frame AVIF images are also supported

## Notes

- **Input GIFs, MP4s, and AVIFs longer than 3 seconds will be trimmed to the first 3 seconds.**
- **Output videos are always encoded at 30 FPS by default** (Telegram standard, iOS requirement).
- For MP4 input, the original FPS is detected and used for frame extraction.
- For AVIF input, FPS is auto-detected from stream metadata.
- Use `--target-fps` to override output FPS (not recommended for Telegram stickers/emoji).
- **If no output filename is specified, the output file is named after the input file with .webm extension** (e.g., `animation.gif` → `animation.webm`).
- Intermediate frames are saved as PNGs in a `frames/` directory (automatically cleaned).
- The `-s`/`--size` parameter sets the longest side to the specified value, preserving the original aspect ratio.
- By default, images are scaled to fit the target size without padding. Use `--pad` to center images on a square transparent canvas.
- The `--blur <value>` option enables border blur with the specified radius. If not set, border is sharp.
- **Transparency is supported for GIF, PNG, and AVIF input. MP4 input does not support transparency.**
- **If emoji mode (`-e`) is enabled, the output file size limit is reduced to 64 KB. Otherwise, the limit is 256 KB.**

## License

MIT License