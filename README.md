# GifToWebM

A simple C# console utility that converts short GIF animations (up to 3 seconds) into optimized WebM videos using FFmpeg.

## Features

- Extracts and scales frames from a GIF.
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
| `-i`, `--input`      | Input GIF file (default: `input.gif`)     |
| `-o`, `--output`     | Output WebM file (default: `output.webm`) |
| `-c`, `--crf-step`   | CRF step increment (default: `2`)         |
| `-b`, `--border`   | Set outline for image (default: disabled)         |
| `--border-size`   | Outline thickness in pixels (default: `2`)         |
| `--border-color`   | Outline color hex (default: `#FFFFFF`)         |
| `--fps`			| Set FPS for output video (default: `10`). Autocalculated for gifs         |
| `-h`, `--help`       | Show help                          |

## Example

This command will convert `animation.gif` to `result.webm`, increasing the CRF by 5 on each iteration until the file size is small enough. Border will be added to the image with a thickness of 4 pixels.
```
GifToWebM.exe -i animation.gif -o result.webm -c 5 -b --border-size 4
```

This comman will convert png sequence to `result.webm`. Border will be added to the image with a thickness of 4 pixels and red color.
```
GifToWebM.exe -i "path\to\first_frame.png" -o result.webm -b --border-size 4 --border-color #FF0000
```

## Notes

- Input GIFs longer than 3 seconds are not allowed.
- Output is saved in `output.webm` unless otherwise specified.
- Intermediate frames are saved as PNGs in a `frames/` directory (automatically cleaned).

## License

MIT License