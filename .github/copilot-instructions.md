# GifToWebM Converter - AI Agent Instructions

## Project Overview
**Purpose**: Create Telegram stickers and emoji with strict platform requirements.
Single-file console application that converts GIF/MP4/AVIF/PNG sequences to optimized WebM videos. Built on .NET Framework 4.7.2 with WPF for image processing and FFmpeg for video encoding.

### Telegram Requirements (Critical)
- **File size**: Max 256KB (standard), 64KB (emoji mode)
- **Target FPS**: 30 FPS (Telegram standard - visible issues on iOS if different)
- **Duration**: Max 3 seconds output (input can be 4s, see Roadmap)
- **Format**: WebM with VP9 codec + alpha channel (yuva420p)
- **Real testing**: Only uploading to Telegram reveals actual compatibility

## Architecture Patterns

### Single Monolithic File Design
All logic lives in `Program.cs` (~1000 lines). No separation of concerns - this is intentional for deployment simplicity. When adding features:
- Add new static helper methods to the `Program` class
- Keep Main() as the orchestrator that calls helpers sequentially
- No external class libraries or modules

### FFmpeg Integration
FFmpeg/FFprobe executables are embedded in project (copied to output via `.csproj`):
```xml
<Content Include="ffmpeg.exe">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
```
Always reference as: `Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe")`

### Image Processing Stack
Uses **WPF imaging APIs** (not System.Drawing or FFmpeg scaling) for alpha channel preservation:
- `BitmapImage` for loading
- `FormatConvertedBitmap` for pixel format conversion (Bgra32/Bgr32)
- `TransformedBitmap` for scaling
- `RenderTargetBitmap` + `DrawingVisual` for compositing with transparent backgrounds
- Always call `bitmap.Freeze()` after loading for thread-safety

**Critical**: WPF is required for scaling because FFmpeg's scale filter produces white artifacts at edges when resizing images with alpha channel due to incorrect/uneven alpha channel scaling. WPF handles this correctly.

## Critical Workflows

### Format-Specific Frame Extraction

**GIF**: Calculate FPS from frame delays before extraction
```csharp
GifBitmapDecoder decoder = new GifBitmapDecoder(fs, ...);
// Read /grctlext/Delay metadata (in centiseconds)
fps = gifFrameCount / totalDelay;
```

**AVIF**: Multi-stream detection is complex
```csharp
// 1. Detect ALL video streams with ffprobe
// 2. Find stream with highest FPS (animated stream)
// 3. Use alphamerge filter to combine color + alpha streams:
//    -filter_complex "[0:colorIdx][0:alphaIdx]alphamerge[out]"
```

**MP4**: Direct extraction, preserve original FPS
```csharp
// Always limit to 3 seconds: -t 3
```

### Size Optimization Loop
WebM encoding uses iterative CRF adjustment:
```csharp
int maxOutputSize = emojiMode ? 64 * 1024 : 256 * 1024;
while (fileSize > maxOutputSize) {
    crf += crfStep;  // Increase = worse quality, smaller file
    // Re-encode with new CRF
}
```
Start at CRF=30, increment by `crfStep` (default 2) until size fits.

### Border Algorithm Pattern
Three-phase pixel manipulation (see `AddBorder()`):
1. **Dilate alpha channel**: Expand alpha using max filter over `borderSize` neighborhood
2. **Create border mask**: `borderMask = dilatedAlpha - originalAlpha`
3. **Blur mask** (optional): Box blur over `blurRadius` neighborhood
4. **Composite**: Alpha blend original over border using standard formula

## Code Conventions

### Change Documentation
- **All changes must be documented in `CHANGELOG.md`**
- Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/)
- Uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
- Add new changes to `[Unreleased]` section with categories:
  - `Added` - new features
  - `Changed` - changes in existing functionality
  - `Fixed` - bug fixes
  - `Removed` - removed features
- **Do not create separate change log files** - update existing `CHANGELOG.md`

### Error Handling
- No exceptions thrown - return early with `Console.WriteLine()` + `return`
- User errors (bad args) → print error + `PrintHelp()` + `return`
- FFmpeg failures → print stderr output but don't crash

### Naming Patterns
- Frame files: `frame_000.png`, `frame_001.png` (3-digit zero-padded)
- Temp directory: `frames/temp/` (deleted after processing)
- Boolean flags: `addBorder`, `addPadding`, `emojiMode` (verb + noun)

### Command-Line Parsing
Manual switch-case loop. When adding new options:
```csharp
case "--new-option":
    if (i + 1 < args.Length && int.TryParse(args[++i], out int val))
        newOption = val;
    else {
        Console.WriteLine("Error: Invalid value for new option.");
        return;
    }
    break;
```
Always validate and consume next arg with `++i`.

## Build & Dependencies

Build via Visual Studio or:
```bash
msbuild Converter.csproj /p:Configuration=Release
```

**CI/CD**: Automated builds through Gitea Actions on tag push:
- Workflow: `.gitea/workflows/build-release.yml`
- Triggers on: Any tag push (e.g., `git tag v1.0.0 && git push --tags`)
- Output: `converter-{tag}.zip` uploaded to Gitea release
- Uses Gitea secrets: `TOKEN` (for API access)
- Uses Gitea variables: `SOLUTION`, `URL` (Gitea instance URL)

**External dependencies**: None (NuGet packages). Only system assemblies:
- `PresentationCore`, `PresentationFramework`, `WindowsBase` (WPF)
- `System`, `System.Core`, `System.Xml`

**Runtime requirement**: FFmpeg/FFprobe must support:
- VP9 codec (`libvpx-vp9`)
- AVIF decoding (`libaom`/`libdav1d`) for AVIF input
- Any standard FFmpeg build works; version doesn't matter

## Key Constraints

1. **3-second maximum**: All video inputs trimmed to first 3 seconds (hardcoded `-t 3`)
2. **Output size limits**: 256KB standard, 64KB emoji mode (enforced via CRF loop)
3. **WPF required**: Cannot port to .NET Core without replacing WPF imaging APIs
4. **No async**: All I/O is synchronous (Process.Start → WaitForExit)

## Roadmap Features

### Planned: 4-second Input with Speed Adjustment
- Accept input videos up to 4 seconds
- **Only apply speed adjustment if input is between 3-4 seconds**
  - If input ≤3s: Keep original speed and duration
  - If input >3s and ≤4s: Speed up to fit into 3 seconds
- Automatically adjust to target 30 FPS for Telegram compatibility
- Implementation approach:
  ```csharp
  // 1. Detect duration with ffprobe
  // 2. If duration > 3.0 && duration <= 4.0:
  //    - Calculate speed multiplier: inputDuration / 3.0
  //    - Apply setpts filter: -vf "setpts=PTS/{speedMultiplier}"
  //    - Force output to 30 FPS: -r 30
  // 3. Else if duration <= 3.0:
  //    - Extract as-is, no speed adjustment
  ```

## Testing Approach
No unit tests. Manual testing workflow:
1. Test each format: `Converter.exe -i test.{gif,mp4,avif,png}`
2. Verify transparency: Load output WebM in browser, check alpha
3. Check size: `ls -lh output.webm` → must be ≤256KB
4. Validate FPS: Use MediaInfo or ffprobe on output
5. **Real validation**: Upload to Telegram as sticker/emoji
   - iOS testing is critical (30 FPS requirement)
   - Desktop/Android more forgiving with FPS mismatches

## Common Pitfalls

- **Don't use System.Drawing.Bitmap**: Loses alpha channel
- **GIF frame delays**: Stored in centiseconds (÷100 to get seconds)
- **AVIF streams**: Color/alpha are separate - must use alphamerge filter
- **Temp directory cleanup**: Call `GC.Collect()` before deleting (releases file handles)
- **PNG sequence mode**: Requires one `.png` input; scans directory for all PNGs
