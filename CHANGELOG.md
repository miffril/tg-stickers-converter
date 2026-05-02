# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.5.0] - 2025-16-12

## [0.6.0] - 2026-05-02

### Added
- `--allow-speedup` option to accept GIF/MP4/AVIF inputs between 3 and 5 seconds and speed them up to 3 seconds during WebM encoding

### Changed
- Inputs longer than 3 seconds now stop by default instead of silently trimming video files
- For 3-5 second inputs, the app now tells the user to use `--allow-speedup` when speedup is available
- Speedup mode now targets a slightly shorter output duration to stay under 3 seconds without hard trimming looped animations
- Speedup safety margin is now based on target FPS so final output stays below 3 seconds more reliably
- Speedup mode now verifies the actual encoded duration and increases acceleration iteratively until the output is at or below 3 seconds
- The converter no longer waits for a key press before exiting and now reports the final achieved speedup multiplier

### Fixed
- **[WORKAROUND] iOS chroma artifacts**: Added automatic 2px top padding for non-square videos
  - **Problem**: Green/purple bands appear at top edge on iOS Telegram due to yuv420p chroma subsampling
  - **Solution**: Adds 2px transparent padding at top (moves content away from problematic edge)
  - **When applied**: Only when `--pad` flag is NOT used
  - **Visibility**: 2px offset is invisible to human eye but sufficient to prevent artifacts
  - **Limitations**: Does not apply if resulting height would exceed target size
  - **Why workaround**: Root cause is Telegram's yuv420p requirement + iOS strict decoder
  - **Tested**: Confirmed to fix artifacts on actual iOS Telegram app

### Added
- **Separate FPS control parameters**:
  - `--fps` / `--input-fps`: Controls frame extraction FPS (default: 10, auto-detected for GIF/MP4/AVIF)
  - `--target-fps` / `--output-fps`: Controls output WebM FPS (default: 30)
- Improved user feedback: Shows both input and target FPS during conversion
- Warning message when using non-standard target FPS (not 30)

### Changed
- **Output WebM now defaults to 30 FPS** (Telegram standard, iOS requirement)
- FFmpeg command now uses `-r {targetFps}` to enforce output framerate
- `--fps` parameter is now an alias for `--input-fps` (backward compatible)
- Updated help text to clarify FPS parameter purposes
- **ScaleAndPadToSquare behavior**: Now adds 2px top padding by default (when addPadding=false)

### Fixed
- **Fixed iOS Telegram playback issue**: Videos with 60+ FPS no longer play slower on iOS
- Videos now maintain correct playback speed regardless of source FPS

## [0.5.0] - 2025-16-12

### Added
- **AVIF format support**: Full support for both static and animated AVIF files
  - Multi-stream detection: Automatically identifies animated streams in AVIF containers
  - Alpha channel support: Merges separate color and alpha streams using `alphamerge` filter
  - Frame rate detection: Automatically detects FPS from AVIF metadata
  - Respects 3-second maximum duration limit for animated AVIF
  - Requires ffmpeg built with AVIF support (libaom/libdav1d)

### Changed
- Updated input validation to accept `.avif` file extension
- Enhanced help message to include AVIF in supported formats list
- **Output filename now automatically generated from input filename when `--output` is not specified**

### Fixed
- **Fixed GIF output duration issue**: GIF frame extraction now correctly uses calculated FPS

## [0.4.1] - 2025-01-XX

### Changed
- **BREAKING**: Default behavior now scales images proportionally without adding padding to square canvas
  - Images are scaled so that the longest side equals the target size (default: 512px)
  - The other dimension will be ? target size, preserving aspect ratio
  - No transparent padding is added by default

### Added
- New `--pad` (`-p`) option to enable padding to square canvas with transparent background
  - When enabled, images are centered on a square canvas (e.g., 512x512)
  - Preserves the old behavior for users who need it

### Fixed
- Fixed issue where non-square images (e.g., 512x289) were unnecessarily padded to square canvas
- Improved scaling logic to handle edge cases where one dimension already matches target size

## [0.4.0] - Previous Release

### Features
- Convert GIF, MP4, or PNG sequences to WebM format
- Automatic FPS detection for GIF and MP4inputs
- Customizable target size (default: 512px)
- Emoji mode for 100x100 output
- Optional border with customizable color and size
- Border blur effect with adjustable radius
- Automatic CRF adjustment to meet file size limits (256 KB or 64 KB for emoji mode)
- Support for transparency in GIF and PNG inputs
- Automatic trimming of input videos to 3 seconds maximum
