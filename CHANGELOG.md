# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
