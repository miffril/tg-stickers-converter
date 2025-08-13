# GifToWebM

A simple C# console utility that converts short GIF animations (up to 3 seconds) into optimized WebM videos using FFmpeg.

## Features

- Extracts and scales frames from a GIF or PNG sequence.
- Allows specifying output frame size in pixels (default: 512x512, 1:1 aspect ratio).
- Automatically replaces black frames with the last valid one.
- Calculates correct FPS based on GIF timing.
- Encodes frames into a VP9 WebM file with alpha channel support.
- Adjusts CRF until output size is under 256 KB.

## Requirements

- .NET (WPF support required)
- `ffmpeg.exe` must be present in the same directory as the executable

## Usage

```
GifToWebM.exe [options]
```

### Options

| Option                | Description                              |
|----------------------|------------------------------------------|
| `-i`, `--input`      | Input GIF file or PNG file (for PNG sequence) |
| `-o`, `--output`     | Output WebM file (default: `output.webm`) |
| `-c`, `--crf-step`   | CRF step increment (default: `2`)         |
| `-b`, `--border`     | Add border to frames (default: disabled)  |
| `--border-size`      | Border thickness in pixels (default: `2`) |
| `--border-color`     | Border color hex (default: `#FFFFFF`)     |
| `--fps`              | Set FPS for output video (default: `10`). Autocalculated for GIFs |
| `-s`, `--size`       | Target size in pixels for output frames (default: `512`, produces 512x512 frames) |
| `-h`, `--help`       | Show help                                 |

## Example

Convert `animation.gif` to `result.webm` with 100x100 output frames:
```
GifToWebM.exe -i animation.gif -o result.webm -s 100
```

Convert PNG sequence to `result.webm` with a 256x256 output size and a red border:
```
GifToWebM.exe -i "path\to\first_frame.png" -o result.webm -s 256 -b --border-size 4 --border-color #FF0000
```

Convert GIF to WebM with default size (512x512) and no border:
```
GifToWebM.exe -i input.gif -o output.webm
```

Convert GIF to WebM with custom FPS and CRF step:
```
GifToWebM.exe -i input.gif -o output.webm --fps 15 --crf-step 5
```

Convert PNG sequence to WebM with a green border and increased border thickness:
```
GifToWebM.exe -i "frames\frame_001.png" -o output.webm -b --border-size 8 --border-color #00FF00
```

Show help:
```
GifToWebM.exe --help
```

## Notes

- Input GIFs longer than 3 seconds are not allowed.
- Output is saved in `output.webm` unless otherwise specified.
- Intermediate frames are saved as PNGs in a `frames/` directory (automatically cleaned).
- The `-s`/`--size` parameter sets both width and height to the specified value (aspect ratio 1:1).

## License

MIT License