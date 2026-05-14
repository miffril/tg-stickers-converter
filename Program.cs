using System;
using System.Diagnostics;
using System.Globalization;
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
        /// <summary>
        /// Parses command-line arguments and runs the conversion pipeline.
        /// </summary>
        static void Main(string[] args)
        {
            // Default values.
            string inputFile = null;
            InputType inputType = InputType.Unknown;
            string outputVideo = null;
            int crf = 30;
            int crfStep = 2;
            bool addBorder = false;
            int borderSize = 2;
            string borderColorHex = "#FFFFFF";
            int fps = 10; // Input FPS used for frame extraction.
            int targetFps = 30; // Output WebM FPS required by Telegram.
            int targetWidth = 512;
            int targetHeight = 512;
            bool emojiMode = false;
            int blurRadius = 0;
            bool addPadding = false;
            bool allowSpeedup = false;
            bool userSetFps = false; // Tracks whether the user explicitly set the input FPS.
            bool userSetTargetFps = false; // Tracks whether the user explicitly set the target FPS.

            // Parse command-line arguments.
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
                    case "--input-fps":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int inputFps))
                        {
                            fps = inputFps;
                            userSetFps = true;
                        }
                        else
                        {
                            Console.WriteLine("Error: Missing value for input FPS.");
                            return;
                        }
                        break;
                    case "--target-fps":
                    case "--output-fps":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int outFps))
                        {
                            targetFps = outFps;
                            userSetTargetFps = true;
                        }
                        else
                        {
                            Console.WriteLine("Error: Missing value for target FPS.");
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
                    case "--allow-speedup":
                        allowSpeedup = true;
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

            // Determine the input type.
            if (string.IsNullOrEmpty(inputFile))
            {
                Console.WriteLine("Error: No input file specified.");
                PrintHelp();
                return;
            }

            // Generate the output file name if it was not specified.
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
            double inputDuration = 0;
            bool speedupApplied = false;

            // Clear the frames directory if it exists and is not empty.
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
                    // Handle a PNG sequence.
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
                    // Detect the FPS and extract frames by using FFmpeg.
                    string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                    string ffprobePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffprobe.exe");
                    tempFramesDir = Path.Combine(framesDir, "temp");
                    Directory.CreateDirectory(tempFramesDir);

                    if (extension == ".gif")
                    {
                        // Calculate the FPS from GIF metadata.
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
                        inputDuration = totalDelay;
                        if (!ValidateInputDuration("GIF", inputDuration, allowSpeedup, out speedupApplied))
                        {
                            return;
                        }
                        // Only calculate the FPS if the user did not explicitly set it.
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
                        // Get the FPS and duration from the MP4 file.
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
                                double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
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
                        inputDuration = duration;
                        if (!ValidateInputDuration("MP4", inputDuration, allowSpeedup, out speedupApplied))
                        {
                            return;
                        }
                        fps = detectedFps;
                        hasAlpha = false;
                    }
                    else if (extension == ".avif")
                    {
                        // Detect whether the AVIF file is animated by using multiple methods.
                        int avifFrameCount = 1;
                        double duration = 0;
                        int detectedFps = 10;
                        int animatedStreamIndex = -1; // Tracks which stream contains animation.
                        
                        try
                        {
                            // Get the duration first.
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
                                if (!string.IsNullOrEmpty(trimmed) && double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d > 0)
                                {
                                    duration = d;
                                    Console.WriteLine($"AVIF duration: {duration:F2} seconds");
                                }
                            }
                            
                            // Check all video streams and select the one with the highest FPS as the animated stream.
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
                                        // Parse the stream index.
                                        int streamIdx = -1;
                                        int.TryParse(parts[0], out streamIdx);
                                        
                                        // Parse the FPS.
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
                                        
                                        // Parse the frame count if it is available.
                                        int streamFrames = 0;
                                        if (parts.Length >= 3)
                                            int.TryParse(parts[2], out streamFrames);
                                        if (parts.Length >= 4 && streamFrames == 0)
                                            int.TryParse(parts[3], out streamFrames);
                                        
                                        // Select the stream with the highest FPS or frame count.
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
                            
                            // If a duration is available but the frame count is not, calculate it.
                            if (duration > 0.1 && avifFrameCount == 1 && detectedFps > 1)
                            {
                                avifFrameCount = (int)Math.Round(duration * detectedFps);
                                Console.WriteLine($"Calculated {avifFrameCount} frames from duration and FPS");
                            }
                            
                            // If the duration is greater than zero, treat the file as animated.
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

                        inputDuration = duration;
                        if (!ValidateInputDuration("AVIF", inputDuration, allowSpeedup, out speedupApplied))
                        {
                            return;
                        }

                        fps = (avifFrameCount > 1 || duration > 0.1) ? Math.Max(detectedFps, 1) : 1;
                        Console.WriteLine($"AVIF processing: {avifFrameCount} frames, {fps} FPS, {duration:F2}s duration, stream index: {animatedStreamIndex}");
                        hasAlpha = true;
                        
                        // Store the animated stream index for extraction.
                        if (animatedStreamIndex >= 0)
                        {
                            tempFramesDir = $"{Path.Combine(framesDir, "temp")}|{animatedStreamIndex}";
                        }
                    }

                    // Extract frames by using FFmpeg.
                    string extractionDurationArg = GetExtractionDurationArgument(inputDuration, speedupApplied);
                    string extractCmd;
                    if (extension == ".gif")
                    {
                        extractCmd = $"-y -r {fps} -i \"{inputFile}\" \"{Path.Combine(tempFramesDir, "frame_%03d.png")}\"";
                    }
                    else if (extension == ".mp4")
                    {
                        extractCmd = $"-y -i \"{inputFile}\" -t {extractionDurationArg} -vf fps={fps} \"{Path.Combine(tempFramesDir, "frame_%03d.png")}\"";
                    }
                    else // AVIF
                    {
                        // Extract the stream index if it was stored in tempFramesDir.
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
                                // In AVIF, the alpha stream is typically the color stream index plus one.
                                alphaStreamIdx = streamIdx + 1;
                                Console.WriteLine($"Using color stream: {colorStreamIdx}, alpha stream: {alphaStreamIdx}");
                            }
                        }
                        
                        // Recreate the temporary directory by using the correct path.
                        if (actualTempDir != tempFramesDir)
                        {
                            tempFramesDir = actualTempDir;
                            if (Directory.Exists(tempFramesDir))
                                Directory.Delete(tempFramesDir, true);
                            Directory.CreateDirectory(tempFramesDir);
                        }
                        
                        // For AVIF with alpha, use an extraction filter.
                        // AVIF stores color and alpha as separate streams that must be combined.
                        if (colorStreamIdx >= 0 && alphaStreamIdx >= 0)
                        {
                            // Use the alphamerge filter to combine the color and alpha streams.
                            // Limit extraction to the allowed duration and use explicit frame-rate control.
                            extractCmd = $"-y -i \"{inputFile}\" -t {extractionDurationArg} -filter_complex \"[0:{colorStreamIdx}][0:{alphaStreamIdx}]alphamerge[out]\" -map \"[out]\" -r {fps} -pix_fmt rgba \"{Path.Combine(tempFramesDir, "frame_%03d.png")}\"";
                        }
                        else
                        {
                            // Fallback: use default extraction with the allowed duration.
                            extractCmd = $"-y -i \"{inputFile}\" -t {extractionDurationArg} -r {fps} -pix_fmt rgba \"{Path.Combine(tempFramesDir, "frame_%03d.png")}\"";
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
                    
                    // For AVIF, recalculate the FPS from the actual extracted frame count.
                    if (extension == ".avif" && frameCount > 1)
                    {
                        // If more frames were extracted than expected, recalculate the FPS.
                        Console.WriteLine($"Extracted {frameCount} frames from AVIF");
                        if (frameCount > 1)
                        {
                            // Try to get a more accurate duration if it is available.
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

                // Process frames by using WPF imaging to preserve alpha quality.
                // Workaround: ScaleAndPadToSquare adds 2 px of top padding by default to reduce iOS chroma artifacts.
                ProcessFrames(framesToProcess, framesDir, hasAlpha, targetWidth, addPadding, addBorder, borderSize, borderColorHex, blurRadius);

                // Clean up the temporary directory.
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

            // Build the FFmpeg command.
            const double telegramMaxDurationSeconds = 3.0;
            const int maxSpeedupAttempts = 5;
            string ffmpegPathForVideo = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            string ffprobePathForVideo = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffprobe.exe");
            double targetDurationSeconds = telegramMaxDurationSeconds;
            double encodingInputFps = fps;
            int speedupAttempt = 0;
            double finalOutputDuration = 0;
            if (speedupApplied && frameCount > 0)
            {
                encodingInputFps = frameCount / targetDurationSeconds;
                Console.WriteLine($"Applying speedup: {inputDuration:F2}s -> {targetDurationSeconds:F2}s (encoding FPS: {encodingInputFps:F3}).");
            }

            // Report the FPS settings.
            Console.WriteLine($"Input FPS: {fps}, Encoding FPS: {encodingInputFps.ToString("0.###", CultureInfo.InvariantCulture)}, Target output FPS: {targetFps}");
            if (targetFps != 30 && userSetTargetFps)
            {
                Console.WriteLine("Warning: Telegram requires 30 FPS for optimal compatibility (especially on iOS).");
            }
            if (Math.Abs(encodingInputFps - targetFps) > 0.001)
            {
                Console.WriteLine($"FFmpeg will convert from {encodingInputFps.ToString("0.###", CultureInfo.InvariantCulture)} FPS to {targetFps} FPS.");
            }

            int maxOutputSize = emojiMode ? 64 * 1024 : 256 * 1024;

            while (true)
            {
                string encodingInputFpsArg = encodingInputFps.ToString("0.###", CultureInfo.InvariantCulture);
                string argumentsStr = $"-y -framerate {encodingInputFpsArg} -i \"{Path.Combine(framesDir, "frame_%03d.png")}\" -c:v libvpx-vp9 -pix_fmt yuva420p -r {targetFps} -crf {crf} \"{outputVideo}\"";
                Console.WriteLine($"Running ffmpeg with CRF {crf} to create video at {targetFps} FPS...");
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

                if (speedupApplied)
                {
                    double outputDuration = GetMediaDuration(ffprobePathForVideo, outputVideo);
                    finalOutputDuration = outputDuration;
                    if (outputDuration > telegramMaxDurationSeconds)
                    {
                        if (speedupAttempt >= maxSpeedupAttempts)
                        {
                            Console.WriteLine($"Error: Could not reduce output duration to {telegramMaxDurationSeconds:F2} seconds after {maxSpeedupAttempts} attempts.");
                            return;
                        }

                        speedupAttempt++;
                        double previousEncodingInputFps = encodingInputFps;
                        encodingInputFps *= (outputDuration / telegramMaxDurationSeconds) * 1.001;
                        Console.WriteLine($"Output duration is {outputDuration:F3}s. Increasing speedup attempt {speedupAttempt}/{maxSpeedupAttempts}: {previousEncodingInputFps:F3} FPS -> {encodingInputFps:F3} FPS.");
                        continue;
                    }
                }

                FileInfo fileInfo = new FileInfo(outputVideo);
                if (fileInfo.Length <= maxOutputSize)
                {
                    break;
                }

                crf += crfStep;
            }

            if (speedupApplied && inputDuration > 0)
            {
                if (finalOutputDuration <= 0)
                {
                    finalOutputDuration = GetMediaDuration(ffprobePathForVideo, outputVideo);
                }

                if (finalOutputDuration > 0)
                {
                    double speedupMultiplier = inputDuration / finalOutputDuration;
                    Console.WriteLine($"Final speedup: {speedupMultiplier:F3}x ({inputDuration:F3}s -> {finalOutputDuration:F3}s)");
                }
            }

            Console.WriteLine("Video created: " + outputVideo);
        }
        
        /// <summary>
        /// Processes source frames, applies scaling and optional effects, and saves them as PNG files.
        /// </summary>
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
                    if (formattedFrame.PixelWidth == targetSize && formattedFrame.PixelHeight == targetSize)
                    {
                        processedBitmap = formattedFrame;
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
        /// Determines whether the specified <see cref="BitmapSource"/> is completely black.
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
        /// Adds a smooth border to a <see cref="BitmapSource"/> while preserving transparency.
        /// The method dilates the alpha channel, computes the border mask,
        /// optionally applies a box blur to smooth the mask, and composites the original image over the border.
        /// </summary>
        static TransformedBitmap AddBorder(BitmapSource bitmap, int borderSize, string borderColorHex, int blurRadius = 0)
        {
            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;
            int bytesPerPixel = (bitmap.Format.BitsPerPixel + 7) / 8;
            int stride = width * bytesPerPixel;
            byte[] pixels = new byte[height * stride];
            bitmap.CopyPixels(pixels, stride, 0);

            // Save the original alpha channel.
            byte[] origAlpha = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * stride + x * 4;
                    origAlpha[y * width + x] = pixels[idx + 3];
                }
            }

            // Dilate the alpha channel by using borderSize as the radius.
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

            // Compute the border mask as the difference between the dilated alpha and the original alpha.
            byte[] borderMask = new byte[width * height];
            for (int i = 0; i < width * height; i++)
            {
                int diff = dilatedAlpha[i] - origAlpha[i];
                borderMask[i] = (byte)(diff < 0 ? 0 : diff);
            }

            // Apply a blur if blurRadius is greater than zero.
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

            // Create a border image by using the specified color and the mask as alpha.
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

            // Composite the original image over the border by using alpha composition.
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

                    // Alpha composition: result = original + border * (1 - original alpha).
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
        /// Saves the specified <see cref="BitmapSource"/> as a PNG file.
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
            Console.WriteLine("      --fps, --input-fps <value>");
            Console.WriteLine("                           Input FPS for frame extraction (default: 10, auto-calculated for GIF/MP4/AVIF)");
            Console.WriteLine("      --target-fps, --output-fps <value>");
            Console.WriteLine("                           Target output FPS for WebM (default: 30, Telegram standard)");
            Console.WriteLine("  -s, --size <value>       Target size in pixels (default: 512, 1:1 aspect ratio)");
            Console.WriteLine("  -p, --pad                Add padding to square canvas (default: disabled)");
            Console.WriteLine("  -e, --emoji              Set target size to 100x100 for emoji output");
            Console.WriteLine("      --allow-speedup      Allow 3-5 second GIF/MP4/AVIF inputs and speed them up to 3 seconds");
            Console.WriteLine("  -h, --help               Display this help message");
        }

        /// <summary>
        /// Validates the input duration against Telegram limits and determines whether speedup should be applied.
        /// </summary>
        static bool ValidateInputDuration(string inputLabel, double duration, bool allowSpeedup, out bool speedupApplied)
        {
            speedupApplied = false;

            if (duration <= 0)
            {
                return true;
            }

            if (duration <= 3.0)
            {
                return true;
            }

            if (duration <= 5.0)
            {
                Console.WriteLine($"Error: Input {inputLabel} duration is {duration:F2} seconds, which exceeds Telegram's 3 second limit.");
                if (!allowSpeedup)
                {
                    Console.WriteLine("Tip: Use --allow-speedup to speed up inputs between 3 and 5 seconds down to 3 seconds.");
                    return false;
                }

                speedupApplied = true;
                Console.WriteLine("--allow-speedup enabled. The input will be accelerated to 3 seconds during encoding.");
                return true;
            }

            Console.WriteLine($"Error: Input {inputLabel} duration is {duration:F2} seconds, which exceeds the supported 5 second limit.");
            if (!allowSpeedup)
            {
                Console.WriteLine("Tip: --allow-speedup only supports inputs up to 5 seconds.");
            }
            return false;
        }

        /// <summary>
        /// Returns the FFmpeg extraction duration argument for the current input.
        /// </summary>
        static string GetExtractionDurationArgument(double inputDuration, bool speedupApplied)
        {
            double extractionDuration = inputDuration > 0
                ? (speedupApplied ? inputDuration : Math.Min(inputDuration, 3.0))
                : 3.0;

            return extractionDuration.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Gets the media duration by using FFprobe.
        /// </summary>
        static double GetMediaDuration(string ffprobePath, string mediaPath)
        {
            try
            {
                string probeArgs = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{mediaPath}\"";
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
                    if (double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double duration))
                    {
                        return duration;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not determine output duration: {ex.Message}");
            }

            return 0;
        }

        /// <summary>
        /// Scales and optionally pads a <see cref="BitmapSource"/> to fit within the target size while preserving the aspect ratio.
        /// </summary>
        /// <remarks>
        /// Workaround for iOS chroma artifacts: when addPadding is false, the method adds 2 px of transparent padding at the top.
        /// This moves content away from the top edge where Telegram on iOS can show green or purple chroma artifacts
        /// caused by yuv420p subsampling. The 2 px offset is visually negligible but sufficient to prevent artifacts.
        /// </remarks>
        static BitmapSource ScaleAndPadToSquare(BitmapSource source, int targetSize, bool addPadding = false)
        {
            int srcWidth = source.PixelWidth;
            int srcHeight = source.PixelHeight;

            // Calculate the scale so that the larger dimension matches targetSize.
            double scale = (double)targetSize / Math.Max(srcWidth, srcHeight);
            int scaledWidth = (int)Math.Round(srcWidth * scale);
            int scaledHeight = (int)Math.Round(srcHeight * scale);

            // Scale the image proportionally.
            TransformedBitmap scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));

            // Workaround: add 2 px of top padding when --pad is not used to reduce iOS chroma artifacts.
            if (!addPadding)
            {
                // Add minimal transparent padding at the top to move content away from the edge
                // where Telegram on iOS can show green or purple bands because of yuv420p chroma subsampling.
                const int topPadding = 2;
                if (scaledHeight + topPadding <= targetSize)
                {
                    DrawingVisual visual = new DrawingVisual();
                    using (DrawingContext dc = visual.RenderOpen())
                    {
                        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, scaledWidth, scaledHeight + topPadding));
                        // Draw the image with a topPadding-pixel downward offset.
                        dc.DrawImage(scaled, new Rect(0, topPadding, scaledWidth, scaledHeight));
                    }
                    RenderTargetBitmap result = new RenderTargetBitmap(scaledWidth, scaledHeight + topPadding, source.DpiX, source.DpiY, PixelFormats.Pbgra32);
                    result.Render(visual);
                    return result;
                }
                else
                {
                    // There is no room for padding because it would exceed targetSize, so return the scaled image.
                    return scaled;
                }
            }

            // Full padding mode: center the image on a square transparent canvas.
            DrawingVisual visual2 = new DrawingVisual();
            using (DrawingContext dc = visual2.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, targetSize, targetSize));
                // Center the image.
                double offsetX = (targetSize - scaledWidth) / 2.0;
                double offsetY = (targetSize - scaledHeight) / 2.0;
                dc.DrawImage(scaled, new Rect(offsetX, offsetY, scaledWidth, scaledHeight));
            }
            RenderTargetBitmap result2 = new RenderTargetBitmap(targetSize, targetSize, source.DpiX, source.DpiY, PixelFormats.Pbgra32);
            result2.Render(visual2);
            return result2;
        }
    }
}
