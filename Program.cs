using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GifToWebM
{
    enum InputType
    {
        Unknown,
        PngSequence,
        VideoFile
    }

    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Default settings
            string inputFile = null;
            InputType inputType = InputType.Unknown;
            string outputVideo = null;
            int crf = 30;
            int crfStep = 2;
            bool addBorder = false;
            int borderSize = 2;
            string borderColorHex = "#FFFFFF";
            int fps = 10;
            int targetWidth = 512;
            int targetHeight = 512;
            bool emojiMode = false;
            int blurRadius = 0;
            bool addPadding = false;
            bool userSetFps = false; // Track if user explicitly set FPS

            // Parse command-line arguments
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-i":
                    case "--input":
                        if (i + 1 < args.Length)
                        {
                            inputFile = args[++i];
                        }
                        else
                        {
                            Console.WriteLine("Error: Missing value for input file.");
                            return;
                        }
                        break;
                    case "-o":
                    case "--output":
                        if (i + 1 < args.Length)
                        {
                            outputVideo = args[++i];
                        }
                        else
                        {
                            Console.WriteLine("Error: Missing value for output file.");
                            return;
                        }
                        break;
                    case "-c":
                    case "--crf-step":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int step))
                        {
                            crfStep = step;
                        }
                        else
                        {
                            Console.WriteLine("Error: Invalid value for CRF step.");
                            return;
                        }
                        break;
                    case "-b":
                    case "--border":
                        addBorder = true;
                        break;
                    case "--border-size":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int size))
                        {
                            borderSize = size;
                        }
                        else
                        {
                            Console.WriteLine("Error: Invalid value for border size.");
                            return;
                        }
                        break;
                    case "--border-color":
                        if (i + 1 < args.Length)
                        {
                            borderColorHex = args[++i];
                        }
                        else
                        {
                            Console.WriteLine("Error: Missing value for border color.");
                            return;
                        }
                        break;
                    case "--blur":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedBlur))
                        {
                            blurRadius = parsedBlur;
                            i++;
                        }
                        else
                        {
                            Console.WriteLine("Error: --blur requires an integer value.");
                            return;
                        }
                        break;
                    case "--fps":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int inputFps))
                        {
                            fps = inputFps;
                            userSetFps = true;
                        }
                        else
                        {
                            Console.WriteLine("Error: Missing value for FPS.");
                            return;
                        }
                        break;
                    case "-p":
                    case "--pad":
                        addPadding = true;
                        break;
                    case "-e":
                    case "--emoji":
                        targetWidth = 100;
                        targetHeight = 100;
                        emojiMode = true;
                        break;
                    case "--size":
                    case "-s":
                        if (!emojiMode)
                        {
                            if (i + 1 < args.Length && int.TryParse(args[++i], out int parsedSize))
                            {
                                targetWidth = parsedSize;
                                targetHeight = parsedSize;
                            }
                            else
                            {
                                Console.WriteLine("Error: Invalid value for size.");
                                return;
                            }
                        }
                        break;
                    case "-h":
                    case "--help":
                        PrintHelp();
                        return;
                    default:
                        Console.WriteLine($"Error: Unknown argument {args[i]}");
                        PrintHelp();
                        return;
                }
            }

            // Determine input type
            if (string.IsNullOrEmpty(inputFile))
            {
                Console.WriteLine("Error: No input file specified.");
                PrintHelp();
                return;
            }

            // Generate output filename if not specified
            if (string.IsNullOrEmpty(outputVideo))
            {
                string inputFileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputFile);
                outputVideo = inputFileNameWithoutExtension + ".webm";
            }

            string extension = Path.GetExtension(inputFile).ToLowerInvariant();
            if (extension == ".png")
            {
                inputType = InputType.PngSequence;
            }
            else if (extension == ".gif" || extension == ".mp4" || extension == ".avif")
            {
                inputType = InputType.VideoFile;
                if (!File.Exists(inputFile))
                {
                    Console.WriteLine($"Error: Input file '{inputFile}' not found.");
                    PrintHelp();
                    return;
                }
            }
            else
            {
                Console.WriteLine($"Error: Unsupported file format '{extension}'.");
                PrintHelp();
                return;
            }

            string framesDir = "frames";

            // Clear frames directory if it exists and is not empty
            if (Directory.Exists(framesDir) && Directory.GetFiles(framesDir).Length > 0)
            {
                DirectoryInfo di = new DirectoryInfo(framesDir);
                foreach (FileInfo file in di.GetFiles())
                {
                    file.Delete();
                }
                foreach (DirectoryInfo dir in di.GetDirectories())
                {
                    dir.Delete(true);
                }
            }

            if (!Directory.Exists(framesDir))
                Directory.CreateDirectory(framesDir);

            int frameCount = 0;
            string[] framesToProcess = null;
            bool hasAlpha = true;
            string tempFramesDir = null;

            try
            {
                if (inputType == InputType.PngSequence)
                {
                    // Handle PNG sequence
                    string directory = Path.GetDirectoryName(Path.GetFullPath(inputFile));
                    framesToProcess = Directory.GetFiles(directory, "*.png");
                    if (framesToProcess.Length == 0)
                    {
                        Console.WriteLine("Error: No PNG files found.");
                        return;
                    }
                    Array.Sort(framesToProcess);
                    frameCount = framesToProcess.Length;
                    Console.WriteLine($"Found {frameCount} PNG files.");
                }
                else if (inputType == InputType.VideoFile)
                {
                    // Detect FPS and extract frames using ffmpeg
                    string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                    string ffprobePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffprobe.exe");
                    tempFramesDir = Path.Combine(framesDir, "temp");
                    Directory.CreateDirectory(tempFramesDir);

                    if (extension == ".gif")
                    {
                        // Calculate FPS from GIF metadata
                        GifBitmapDecoder decoder;
                        using (FileStream fs = new FileStream(inputFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            decoder = new GifBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                        }
                        int gifFrameCount = decoder.Frames.Count;
                        double totalDelay = 0;
                        for (int i = 0; i < gifFrameCount; i++)
                        {
                            BitmapFrame frame = decoder.Frames[i];
                            double frameDelay = 0.1;
                            if (frame.Metadata is BitmapMetadata metadata)
                            {
                                try
                                {
                                    object delayObj = metadata.GetQuery("/grctlext/Delay");
                                    if (delayObj != null)
                                    {
                                        ushort delay = (ushort)delayObj;
                                        frameDelay = delay / 100.0;
                                    }
                                }
                                catch { }
                            }
                            totalDelay += frameDelay;
                        }
                        if (totalDelay > 3.0)
                        {
                            Console.WriteLine("Error: Input GIF duration exceeds 3 seconds.");
                            return;
                        }
                        // Only calculate FPS if user didn't explicitly set it
                        if (!userSetFps)
                        {
                            fps = (int)((totalDelay > 0) ? gifFrameCount / totalDelay : 10);
                            Console.WriteLine($"Calculated FPS: {fps}");
                        }
                        else
                        {
                            Console.WriteLine($"Using user-specified FPS: {fps}");
                        }
                        hasAlpha = true;
                    }
                    else if (extension == ".mp4")
                    {
                        // Get FPS and duration from MP4
                        double duration = 0;
                        int detectedFps = 10;
                        try
                        {
                            string probeArgs = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{inputFile}\"";
                            using (Process proc = Process.Start(new ProcessStartInfo
                            {
                                FileName = ffprobePath,
                                Arguments = probeArgs,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            }))
                            {
                                string output = proc.StandardOutput.ReadToEnd();
                                proc.WaitForExit();
                                double.TryParse(output.Trim(), out duration);
                            }

                            string fpsProbeArgs = $"-v 0 -select_streams v:0 -show_entries stream=r_frame_rate -of csv=p=0 \"{inputFile}\"";
                            using (Process proc = Process.Start(new ProcessStartInfo
                            {
                                FileName = ffprobePath,
                                Arguments = fpsProbeArgs,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            }))
                            {
                                string output = proc.StandardOutput.ReadToEnd();
                                proc.WaitForExit();
                                string fpsStr = output.Trim();
                                if (fpsStr.Contains("/"))
                                {
                                    var parts = fpsStr.Split('/');
                                    if (parts.Length == 2 && double.TryParse(parts[0], out double num) && double.TryParse(parts[1], out double denom) && denom != 0)
                                    {
                                        detectedFps = (int)Math.Round(num / denom);
                                    }
                                }
                                else
                                {
                                    if (double.TryParse(fpsStr, out double fpsVal))
                                        detectedFps = (int)Math.Round(fpsVal);
                                }
                            }
                        }
                        catch
                        {
                            Console.WriteLine("Warning: Could not determine video properties. Using defaults.");
                        }
                        if (duration > 3.0)
                        {
                            Console.WriteLine("Warning: Input MP4 duration exceeds 3 seconds. Only first 3 seconds will be used.");
                        }
                        fps = detectedFps;
                        hasAlpha = false;
                    }
                    else if (extension == ".avif")
                    {
                        // Detect if AVIF is animated using multiple methods
                        int avifFrameCount = 1;
                        double duration = 0;
                        int detectedFps = 10;
                        int animatedStreamIndex = -1; // Track which stream contains animation
                        
                        try
                        {
                            // Get duration first
                            string probeArgs = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{inputFile}\"";
                            using (Process proc = Process.Start(new ProcessStartInfo
                            {
                                FileName = ffprobePath,
                                Arguments = probeArgs,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            }))
                            {
                                string output = proc.StandardOutput.ReadToEnd();
                                proc.WaitForExit();
                                string trimmed = output.Trim();
                                if (!string.IsNullOrEmpty(trimmed) && double.TryParse(trimmed, out double d) && d > 0)
                                {
                                    duration = d;
                                    Console.WriteLine($"AVIF duration: {duration:F2} seconds");
                                }
                            }
                            
                            // Check all video streams for the one with highest FPS (animated stream)
                            string allStreamsArgs = $"-v error -select_streams v -show_entries stream=index,r_frame_rate,nb_frames,nb_read_frames -of csv=p=0 \"{inputFile}\"";
                            using (Process proc = Process.Start(new ProcessStartInfo
                            {
                                FileName = ffprobePath,
                                Arguments = allStreamsArgs,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            }))
                            {
                                string output = proc.StandardOutput.ReadToEnd();
                                proc.WaitForExit();
                                string[] streams = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                                
                                double maxFps = 0;
                                int maxFrames = 0;
                                
                                foreach (string stream in streams)
                                {
                                    string[] parts = stream.Split(',');
                                    if (parts.Length >= 2)
                                    {
                                        // Parse stream index
                                        int streamIdx = -1;
                                        int.TryParse(parts[0], out streamIdx);
                                        
                                        // Parse FPS
                                        string fpsStr = parts[1];
                                        double streamFps = 0;
                                        if (fpsStr.Contains("/"))
                                        {
                                            var fpsParts = fpsStr.Split('/');
                                            if (fpsParts.Length == 2 && double.TryParse(fpsParts[0], out double num) && double.TryParse(fpsParts[1], out double denom) && denom != 0)
                                            {
                                                streamFps = num / denom;
                                            }
                                        }
                                        else
                                        {
                                            double.TryParse(fpsStr, out streamFps);
                                        }
                                        
                                        // Parse frame count if available
                                        int streamFrames = 0;
                                        if (parts.Length >= 3)
                                            int.TryParse(parts[2], out streamFrames);
                                        if (parts.Length >= 4 && streamFrames == 0)
                                            int.TryParse(parts[3], out streamFrames);
                                        
                                        // Select stream with highest FPS or frame count
                                        if (streamFps > maxFps || (streamFps == maxFps && streamFrames > maxFrames))
                                        {
                                            maxFps = streamFps;
                                            maxFrames = streamFrames;
                                            animatedStreamIndex = streamIdx;
                                            detectedFps = (int)Math.Round(streamFps);
                                            if (streamFrames > 0)
                                                avifFrameCount = streamFrames;
                                        }
                                        
                                        Console.WriteLine($"Stream {streamIdx}: FPS={streamFps:F2}, Frames={streamFrames}");
                                    }
                                }
                                
                                if (animatedStreamIndex >= 0)
                                {
                                    Console.WriteLine($"Selected stream {animatedStreamIndex} with {detectedFps} FPS");
                                }
                            }
                            
                            // If we have duration but no frame count, calculate it
                            if (duration > 0.1 && avifFrameCount == 1 && detectedFps > 1)
                            {
                                avifFrameCount = (int)Math.Round(duration * detectedFps);
                                Console.WriteLine($"Calculated {avifFrameCount} frames from duration and FPS");
                            }
                            
                            // If duration > 0, assume it's animated
                            if (duration > 0.1 && avifFrameCount == 1)
                            {
                                Console.WriteLine("AVIF has duration > 0, treating as animated");
                                avifFrameCount = 2;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Warning: Could not determine AVIF properties: {ex.Message}");
                        }
                        
                        if (duration > 3.0)
                        {
                            Console.WriteLine("Warning: Input AVIF duration exceeds 3 seconds. Only first 3 seconds will be used.");
                        }
                        
                        fps = (avifFrameCount > 1 || duration > 0.1) ? Math.Max(detectedFps, 1) : 1;
                        Console.WriteLine($"AVIF processing: {avifFrameCount} frames, {fps} FPS, {duration:F2}s duration, stream index: {animatedStreamIndex}");
                        hasAlpha = true;
                        
                        // Store animated stream index for extraction
                        if (animatedStreamIndex >= 0)
                        {
                            tempFramesDir = $"{Path.Combine(framesDir, "temp")}|{animatedStreamIndex}";
                        }
                    }

                    // Extract frames using ffmpeg
                    string extractCmd;
                    if (extension == ".gif")
                    {
                        extractCmd = $"-y -r {fps} -i \"{inputFile}\" \"{Path.Combine(tempFramesDir, "frame_%03d.png")}\"";
                    }
                    else if (extension == ".mp4")
                    {
                        extractCmd = $"-y -i \"{inputFile}\" -t 3 -vf fps={fps} \"{Path.Combine(tempFramesDir, "frame_%03d.png")}\"";
                    }
                    else // AVIF
                    {
                        // Extract stream index if stored in tempFramesDir
                        string actualTempDir = tempFramesDir;
                        int colorStreamIdx = -1;
                        int alphaStreamIdx = -1;
                        
                        if (tempFramesDir.Contains("|"))
                        {
                            string[] parts = tempFramesDir.Split('|');
                            actualTempDir = parts[0];
                            if (parts.Length > 1 && int.TryParse(parts[1], out int streamIdx))
                            {
                                colorStreamIdx = streamIdx;
                                // Alpha stream is typically color stream + 1 in AVIF
                                alphaStreamIdx = streamIdx + 1;
                                Console.WriteLine($"Using color stream: {colorStreamIdx}, alpha stream: {alphaStreamIdx}");
                            }
                        }
                        
                        // Recreate temp directory with correct path
                        if (actualTempDir != tempFramesDir)
                        {
                            tempFramesDir = actualTempDir;
                            if (Directory.Exists(tempFramesDir))
                                Directory.Delete(tempFramesDir, true);
                            Directory.CreateDirectory(tempFramesDir);
                        }
                        
                        // For AVIF with alpha, we need to use alpha extraction filter
                        // AVIF stores color and alpha as separate streams that need to be combined
                        if (colorStreamIdx >= 0 && alphaStreamIdx >= 0)
                        {
                            // Use alphamerge filter to combine color and alpha streams
                            // Limit to 3 seconds and use proper frame rate control
                            extractCmd = $"-y -i \"{inputFile}\" -t 3 -filter_complex \"[0:{colorStreamIdx}][0:{alphaStreamIdx}]alphamerge[out]\" -map \"[out]\" -r {fps} -pix_fmt rgba \"{Path.Combine(tempFramesDir, "frame_%03d.png")}\"";
                        }
                        else
                        {
                            // Fallback: try default extraction with 3 second limit
                            extractCmd = $"-y -i \"{inputFile}\" -t 3 -r {fps} -pix_fmt rgba \"{Path.Combine(tempFramesDir, "frame_%03d.png")}\"";
                        }
                    }

                    Console.WriteLine($"Extracting frames from {extension.ToUpper()} using ffmpeg...");
                    Console.WriteLine($"Command: {extractCmd}");
                    using (Process proc = Process.Start(new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = extractCmd,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }))
                    {
                        string output = proc.StandardError.ReadToEnd();
                        proc.WaitForExit();
                        Console.WriteLine(output);
                    }

                    framesToProcess = Directory.GetFiles(tempFramesDir, "frame_*.png");
                    Array.Sort(framesToProcess);
                    frameCount = framesToProcess.Length;
                    
                    // For AVIF, recalculate FPS based on actual extracted frames
                    if (extension == ".avif" && frameCount > 1)
                    {
                        // If we got more frames than expected, recalculate FPS
                        Console.WriteLine($"Extracted {frameCount} frames from AVIF");
                        if (frameCount > 1)
                        {
                            // Try to get more accurate duration if available
                            try
                            {
                                string durationArgs = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{inputFile}\"";
                                using (Process proc = Process.Start(new ProcessStartInfo
                                {
                                    FileName = ffprobePath,
                                    Arguments = durationArgs,
                                    UseShellExecute = false,
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                    CreateNoWindow = true
                                }))
                                {
                                    string output = proc.StandardOutput.ReadToEnd();
                                    proc.WaitForExit();
                                    if (double.TryParse(output.Trim(), out double actualDuration) && actualDuration > 0)
                                    {
                                        fps = (int)Math.Round(frameCount / actualDuration);
                                        Console.WriteLine($"Recalculated FPS based on extracted frames: {fps}");
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    
                    Console.WriteLine($"Processing {frameCount} extracted frames...");
                }

                // Process frames using WPF imaging (preserves quality with alpha channel)
                ProcessFrames(framesToProcess, framesDir, hasAlpha, targetWidth, addPadding, addBorder, borderSize, borderColorHex, blurRadius);

                // Cleanup temporary directory
                if (tempFramesDir != null)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    try
                    {
                        Directory.Delete(tempFramesDir, true);
                    }
                    catch (IOException ex)
                    {
                        Console.WriteLine($"Warning: Could not delete temporary directory: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error deleting temporary directory: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Critical error: {ex.Message}");
                return;
            }

            // Build ffmpeg command
            string ffmpegPathForVideo = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            string argumentsStr = $"-y -framerate {fps} -i \"{Path.Combine(framesDir, "frame_%03d.png")}\" -c:v libvpx-vp9 -pix_fmt yuva420p -crf {crf} \"{outputVideo}\"";

            int maxOutputSize = emojiMode ? 64 * 1024 : 256 * 1024;

            while (true)
            {
                Console.WriteLine($"Running ffmpeg with CRF {crf} to create video...");
                using (Process proc = Process.Start(new ProcessStartInfo
                {
                    FileName = ffmpegPathForVideo,
                    Arguments = argumentsStr,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }))
                {
                    string output = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    Console.WriteLine(output);
                }

                FileInfo fileInfo = new FileInfo(outputVideo);
                if (fileInfo.Length <= maxOutputSize)
                {
                    break;
                }

                crf += crfStep;
                argumentsStr = $"-y -framerate {fps} -i \"{Path.Combine(framesDir, "frame_%03d.png")}\" -c:v libvpx-vp9 -pix_fmt yuva420p -crf {crf} \"{outputVideo}\"";
            }

            Console.WriteLine("Video created: " + outputVideo);
            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }

        static void ProcessFrames(string[] sourceFrames, string outputDir, bool hasAlpha, int targetSize, bool addPadding, bool addBorder, int borderSize, string borderColorHex, int blurRadius)
        {
            for (int i = 0; i < sourceFrames.Length; i++)
            {
                string pngPath = sourceFrames[i];
                BitmapImage bitmap = null;
                try
                {
                    bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(Path.GetFullPath(pngPath));
                    bitmap.EndInit();
                    bitmap.Freeze();

                    PixelFormat pixelFormat = hasAlpha ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
                    FormatConvertedBitmap formattedFrame = new FormatConvertedBitmap(bitmap, pixelFormat, null, 0);

                    BitmapSource processedBitmap;
                    if (formattedFrame.PixelWidth == formattedFrame.PixelHeight && formattedFrame.PixelWidth == targetSize)
                    {
                        processedBitmap = formattedFrame;
                    }
                    else if (formattedFrame.PixelWidth == formattedFrame.PixelHeight)
                    {
                        processedBitmap = ScaleProportional(formattedFrame, targetSize);
                    }
                    else
                    {
                        processedBitmap = ScaleAndPadToSquare(formattedFrame, targetSize, addPadding);
                    }

                    if (addBorder)
                    {
                        processedBitmap = AddBorder(processedBitmap, borderSize, borderColorHex, blurRadius);
                    }

                    string framePath = Path.Combine(outputDir, $"frame_{i:000}.png");
                    SavePng(processedBitmap, framePath);
                    Console.WriteLine($"Processed and saved frame {i} to {framePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing frame {i}: {ex.Message}");
                }
                finally
                {
                    if (bitmap != null)
                    {
                        bitmap = null;
                    }
                }
            }
        }

        /// <summary>
        /// Checks if a BitmapSource is completely black.
        /// </summary>
        static bool IsBlackFrame(BitmapSource bitmap)
        {
            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;
            int stride = width * ((bitmap.Format.BitsPerPixel + 7) / 8);
            byte[] pixels = new byte[height * stride];
            bitmap.CopyPixels(pixels, stride, 0);

            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0 || pixels[i + 3] != 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Adds a smooth white border to a BitmapSource while preserving transparency.
        /// The method dilates the alpha channel, computes the border mask,
        /// applies a box blur to smooth the mask, and composites the original image over the border.
        /// </summary>
        static TransformedBitmap AddBorder(BitmapSource bitmap, int borderSize, string borderColorHex, int blurRadius = 0)
        {
            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;
            int bytesPerPixel = (bitmap.Format.BitsPerPixel + 7) / 8;
            int stride = width * bytesPerPixel;
            byte[] pixels = new byte[height * stride];
            bitmap.CopyPixels(pixels, stride, 0);

            // Save original alpha channel
            byte[] origAlpha = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * stride + x * 4;
                    origAlpha[y * width + x] = pixels[idx + 3];
                }
            }

            // Dilate the alpha channel with radius = borderSize
            byte[] dilatedAlpha = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte maxAlpha = 0;
                    for (int j = -borderSize; j <= borderSize; j++)
                    {
                        for (int i = -borderSize; i <= borderSize; i++)
                        {
                            int nx = x + i;
                            int ny = y + j;
                            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                            {
                                byte candidate = origAlpha[ny * width + nx];
                                if (candidate > maxAlpha)
                                    maxAlpha = candidate;
                            }
                        }
                    }
                    dilatedAlpha[y * width + x] = maxAlpha;
                }
            }

            // Compute border mask as the difference between dilated alpha and original alpha
            byte[] borderMask = new byte[width * height];
            for (int i = 0; i < width * height; i++)
            {
                int diff = dilatedAlpha[i] - origAlpha[i];
                borderMask[i] = (byte)(diff < 0 ? 0 : diff);
            }

            // Apply blur if blurRadius > 0
            byte[] finalMask = borderMask;
            if (blurRadius > 0)
            {
                byte[] blurredMask = new byte[width * height];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int sum = 0;
                        int count = 0;
                        for (int j = -blurRadius; j <= blurRadius; j++)
                        {
                            for (int i = -blurRadius; i <= blurRadius; i++)
                            {
                                int nx = x + i;
                                int ny = y + j;
                                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                                {
                                    sum += borderMask[ny * width + nx];
                                    count++;
                                }
                            }
                        }
                        blurredMask[y * width + x] = (byte)(sum / count);
                    }
                }
                finalMask = blurredMask;
            }

            // Create border image with the specified color and mask as alpha
            Color borderColor = (Color)ColorConverter.ConvertFromString(borderColorHex);
            byte[] borderPixels = new byte[height * stride];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * stride + x * 4;
                    borderPixels[idx] = borderColor.B;
                    borderPixels[idx + 1] = borderColor.G;
                    borderPixels[idx + 2] = borderColor.R;
                    borderPixels[idx + 3] = finalMask[y * width + x];
                }
            }

            // Composite original image over the border using alpha composition
            byte[] outputPixels = new byte[height * stride];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * stride + x * 4;
                    byte origB = pixels[idx];
                    byte origG = pixels[idx + 1];
                    byte origR = pixels[idx + 2];
                    byte origA = pixels[idx + 3];

                    byte borderB = borderPixels[idx];
                    byte borderG = borderPixels[idx + 1];
                    byte borderR = borderPixels[idx + 2];
                    byte borderA = borderPixels[idx + 3];

                    // Alpha composition: result = original + border*(1 - original alpha)
                    float alphaOrig = origA / 255f;
                    float alphaBorder = borderA / 255f;
                    float outA = alphaOrig + alphaBorder * (1 - alphaOrig);
                    float outR = (origR * alphaOrig + borderR * alphaBorder * (1 - alphaOrig)) / (outA > 0 ? outA : 1);
                    float outG = (origG * alphaOrig + borderG * alphaBorder * (1 - alphaOrig)) / (outA > 0 ? outA : 1);
                    float outB = (origB * alphaOrig + borderB * alphaBorder * (1 - alphaOrig)) / (outA > 0 ? outA : 1);

                    outputPixels[idx] = (byte)Math.Round(outB);
                    outputPixels[idx + 1] = (byte)Math.Round(outG);
                    outputPixels[idx + 2] = (byte)Math.Round(outR);
                    outputPixels[idx + 3] = (byte)Math.Round(outA * 255);
                }
            }

            BitmapSource resultBitmap = BitmapSource.Create(width, height, bitmap.DpiX, bitmap.DpiY, bitmap.Format, null, outputPixels, stride);
            return new TransformedBitmap(resultBitmap, new ScaleTransform(1, 1));
        }

        /// <summary>
        /// Saves a BitmapSource as a PNG file.
        /// </summary>
        static void SavePng(BitmapSource bitmap, string filename)
        {
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = new FileStream(filename, FileMode.Create))
            {
                encoder.Save(stream);
            }
        }

        /// <summary>
        /// Prints the help message.
        /// </summary>
        static void PrintHelp()
        {
            Console.WriteLine("Usage: converter.exe [options]");
            Console.WriteLine("Options:");
            Console.WriteLine("  -i, --input <file>       Input file: GIF, MP4, AVIF, or PNG (for directory of PNGs)");
            Console.WriteLine("  -o, --output <file>      Output WebM file (default: output.webm)");
            Console.WriteLine("  -c, --crf-step <value>   CRF step value (default: 2)");
            Console.WriteLine("  -b, --border             Add border to frames");
            Console.WriteLine("      --border-size <value> Border size in pixels (default: 2)");
            Console.WriteLine("      --border-color <hex>  Border color in hex (default: #FFFFFF)");
            Console.WriteLine("      --blur <value>        Border blur radius (integer, default: 0)");
            Console.WriteLine("      --fps <value>         FPS value (default: 10, auto-calculated for GIF)");
            Console.WriteLine("  -s, --size <value>       Target size in pixels (default: 512, 1:1 aspect ratio)");
            Console.WriteLine("  -p, --pad                Add padding to square canvas (default: disabled)");
            Console.WriteLine("  -e, --emoji              Set target size to 100x100 for emoji output");
            Console.WriteLine("  -h, --help               Display this help message");
        }

        // Helper function to calculate proportional scale
        static TransformedBitmap ScaleProportional(BitmapSource source, int maxSize)
        {
            int srcWidth = source.PixelWidth;
            int srcHeight = source.PixelHeight;
            double scale = (double)maxSize / Math.Max(srcWidth, srcHeight);
            double scaleX = scale;
            double scaleY = scale;
            return new TransformedBitmap(source, new ScaleTransform(scaleX, scaleY));
        }

        // Helper function to calculate proportional scale with optional padding
        // Scales image so that one side equals targetSize and the other is <= targetSize
        // If addPadding is true, centers the image on a square transparent canvas
        static BitmapSource ScaleAndPadToSquare(BitmapSource source, int targetSize, bool addPadding = false)
        {
            int srcWidth = source.PixelWidth;
            int srcHeight = source.PixelHeight;
            
            // Calculate scale so that the larger dimension equals targetSize
            double scale = (double)targetSize / Math.Max(srcWidth, srcHeight);
            int scaledWidth = (int)Math.Round(srcWidth * scale);
            int scaledHeight = (int)Math.Round(srcHeight * scale);

            // Scale the image proportionally
            TransformedBitmap scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));

            // If padding is disabled, return scaled image without padding
            if (!addPadding)
            {
                return scaled;
            }

            // Create a square canvas with transparent background
            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, targetSize, targetSize));
                // Center the image
                double offsetX = (targetSize - scaledWidth) / 2.0;
                double offsetY = (targetSize - scaledHeight) / 2.0;
                dc.DrawImage(scaled, new Rect(offsetX, offsetY, scaledWidth, scaledHeight));
            }
            RenderTargetBitmap result = new RenderTargetBitmap(targetSize, targetSize, source.DpiX, source.DpiY, PixelFormats.Pbgra32);
            result.Render(visual);
            return result;
        }
    }
}
