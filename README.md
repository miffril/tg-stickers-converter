# GifToWebM

A simple C# console utility that converts short GIF animations (up to 3 seconds) into optimized WebM videos using FFmpeg.

## Features

- Extracts and scales frames from a GIF or PNG sequence
- Preserves original image proportions without stretching
- Allows specifying output frame size in pixels (default: 512px for longest side)
- Automatically centers images on transparent background
- Fast emoji mode (-e) for quick 100x100 conversions
- Adds optional smooth border with customizable color and size
- **Supports optional border blur with customizable radius**
- Calculates correct FPS based on GIF timing
- Encodes frames into a VP9 WebM file with alpha channel support
- Adjusts CRF until output size is under 256 KB

## Requirements

- .NET Framework 4.7.2 (WPF support required)
- `ffmpeg.exe` must be present in the same directory as the executable

## Usage

```
Converter.exe [options]
```

### Options

| Option                | Description                                          |
|----------------------|------------------------------------------------------|
| `-i`, `--input`      | Input GIF file or PNG file (for PNG sequence)       |
| `-o`, `--output`     | Output WebM file (default: `output.webm`)           |
| `-c`, `--crf-step`   | CRF step increment (default: `2`)                   |
| `-b`, `--border`     | Add border to frames (default: disabled)            |
| `--border-size`      | Border thickness in pixels (default: `2`)           |
| `--border-color`     | Border color hex (default: `#FFFFFF`)               |
| `--blur <value>`     | Border blur radius (integer, required value; default: no blur) |
| `--fps`             | Set FPS for output video (default: `10`). Autocalculated for GIFs |
| `-s`, `--size`       | Target size in pixels for longest side (default: `512`) |
| `-e`, `--emoji`      | Set target size to 100x100 for emoji output        |
| `-h`, `--help`       | Show help                                          |

## Examples

Convert GIF to WebM with default settings (preserving proportions, max size 512px):
```
Converter.exe -i animation.gif -o result.webm
```

Convert GIF to 100x100 emoji:
```
Converter.exe -i animation.gif -o emoji.webm -e
```

Convert with custom size and white border:
```
Converter.exe -i animation.gif -o result.webm -s 256 -b
```

Convert with custom border color and size:
```
Converter.exe -i animation.gif -o result.webm -b --border-size 4 --border-color "#FF0000"
```

Convert with custom border blur:
```
Converter.exe -i animation.gif -o result.webm -b --blur 2
```

Convert PNG sequence with custom FPS:
```
Converter.exe -i "first_frame.png" -o result.webm --fps 15
```

Show help:
```
Converter.exe --help
```

## Notes

- Input GIFs longer than 3 seconds are not allowed.
- Output is saved in `output.webm` unless otherwise specified.
- Intermediate frames are saved as PNGs in a `frames/` directory (automatically cleaned).
- The `-s`/`--size` parameter sets the longest side to the specified value, preserving the original aspect ratio.
- The `--blur <value>` option enables border blur with the specified radius. If not set, border is sharp.

## License

MIT License