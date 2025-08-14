using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GifToWebM
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Default settings
            string inputGif = null;                // Input GIF file
            string[] inputPngs = null;             // Input PNG files
            string outputVideo = "output.webm";    // Output video file
            int crf = 30;                          // Default CRF value
            int crfStep = 2;                       // CRF step value
            bool addBorder = false;                // Flag to add border
            int borderSize = 2;                    // Border size in pixels
            string borderColorHex = "#FFFFFF";     // Border color (white)
            int fps = 10;                          // Default FPS
            int targetWidth = 512;                 // Target width in pixels (default: 512)
            int targetHeight = 512;                // Target height in pixels (default: 512)
            bool emojiMode = false;                // Emoji mode flag
            int blurRadius = 0;                    // Blur radius for border, default: 0 (no blur)

            // Parse command-line arguments
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-i":
                    case "--input":
                        if (i + 1 < args.Length)
                        {
                            string input = args[++i];
                            if (input.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                            {
                                inputGif = input;
                            }
                            else if (input.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                            {
                                inputPngs = Directory.GetFiles(Path.GetDirectoryName(input), "*.png");
                            }
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
                        }
                        else
                        {
                            Console.WriteLine("Error: Missing value for border color.");
                            return;
                        }
                        break;
                    case "-e":
                    case "--emoji":
                        targetWidth = 100;
                        targetHeight = 100;
                        emojiMode = true;
                        break;
                    case "--size":
                    case "-s":
                        if (!emojiMode) {
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
                        } // else ignore -s if emojiMode is set
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

            // Check if input file exists
            if (inputGif != null && !File.Exists(inputGif))
            {
                Console.WriteLine($"Error: Input file '{inputGif}' not found.");
                PrintHelp();
                return;
            }

            if (inputPngs != null && inputPngs.Length == 0)
            {
                Console.WriteLine("Error: No PNG files found.");
                PrintHelp();
                return;
            }

            string framesDir = "frames";  // Directory for saving frames

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

            // Create frames directory if it does not exist
            if (!Directory.Exists(framesDir))
                Directory.CreateDirectory(framesDir);

            int frameCount = 0;

            if (inputGif != null)
            {
                // First pass: load GIF metadata to calculate FPS
                GifBitmapDecoder decoder;
                using (FileStream fs = new FileStream(inputGif, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    decoder = new GifBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                }

                frameCount = decoder.Frames.Count;
                Console.WriteLine($"Found {frameCount} frames.");

                // Calculate total animation time to determine FPS correctly
                double totalDelay = 0;
                for (int i = 0; i < frameCount; i++)
                {
                    BitmapFrame frame = decoder.Frames[i];
                    double frameDelay = 0.1; // Default (0.1 sec = 10 FPS) if delay is not specified
                    if (frame.Metadata is BitmapMetadata metadata)
                    {
                        object delayObj = null;
                        try
                        {
                            delayObj = metadata.GetQuery("/grctlext/Delay");
                        }
                        catch { }
                        if (delayObj != null)
                        {
                            // Delay is stored as ushort (in hundredths of a second)
                            ushort delay = (ushort)delayObj;
                            frameDelay = delay / 100.0;
                        }
                    }
                    totalDelay += frameDelay;
                }

                // Check GIF duration (error if exceeds 3 seconds)
                if (totalDelay > 3.0)
                {
                    Console.WriteLine("Error: Input GIF duration exceeds 3 seconds.");
                    return;
                }

                // Determine FPS (if totalDelay is zero, default to 10 FPS)
                fps = (int)((totalDelay > 0) ? frameCount / totalDelay : 10);
                Console.WriteLine($"Calculated FPS: {fps}");

                // Second pass: extract frames using ffmpeg
                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                string tempFramesDir = Path.Combine(framesDir, "temp");
                Directory.CreateDirectory(tempFramesDir);

                string extractCmd = $"-y -i \"{inputGif}\" \"{Path.Combine(tempFramesDir, "frame_%03d.png")}\"";
                Console.WriteLine("Extracting frames from GIF using ffmpeg...");
                
                ProcessStartInfo psiExtract = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = extractCmd,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                using (Process proc = Process.Start(psiExtract))
                {
                    string output = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    Console.WriteLine(output);
                }

                // Get extracted frames and process them
                string[] extractedFrames = Directory.GetFiles(tempFramesDir, "frame_*.png");
                Array.Sort(extractedFrames); // Ensure proper order
                frameCount = extractedFrames.Length;
                Console.WriteLine($"Processing {frameCount} extracted frames...");

                // Process each extracted frame
                for (int i = 0; i < frameCount; i++)
                {
                    string pngPath = extractedFrames[i];
                    BitmapImage bitmap = null;
                    try
                    {
                        bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(Path.GetFullPath(pngPath));
                        bitmap.EndInit();
                        bitmap.Freeze(); // Ensure the bitmap is frozen for better memory management
                        
                        // Convert to format with alpha channel
                        FormatConvertedBitmap formattedFrame = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                        
                        // Scale and pad to square
                        BitmapSource scaledBitmap = ScaleAndPadToSquare(formattedFrame, targetWidth);
                        
                        // Add border if required
                        if (addBorder)
                        {
                            scaledBitmap = AddBorder(scaledBitmap, borderSize, borderColorHex, blurRadius);
                        }
                        
                        // Save processed frame
                        string framePath = Path.Combine(framesDir, $"frame_{i:000}.png");
                        SavePng(scaledBitmap, framePath);
                        Console.WriteLine($"Processed and saved frame {i} to {framePath}");
                    }
                    finally
                    {
                        if (bitmap != null)
                        {
                            // Explicitly remove the reference to help with cleanup
                            bitmap = null;
                        }
                    }
                }

                // Force garbage collection to ensure resources are released
                GC.Collect();
                GC.WaitForPendingFinalizers();

                // Cleanup temporary frames
                try
                {
                    Directory.Delete(tempFramesDir, true);
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"Warning: Could not delete temporary directory: {ex.Message}");
                    // Continue execution even if cleanup fails
                }
            }
            else if (inputPngs != null)
            {
                frameCount = inputPngs.Length;
                Console.WriteLine($"Found {frameCount} PNG files.");

                // Process each PNG file: scale and save as PNG
                for (int i = 0; i < frameCount; i++)
                {
                    string pngPath = inputPngs[i];
                    string fullPath = Path.GetFullPath(pngPath);
                    BitmapImage bitmap = new BitmapImage(new Uri(fullPath));

                    // Convert frame to format with alpha channel (Bgra32)
                    FormatConvertedBitmap formattedFrame = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);

                    BitmapSource processedBitmap;
                    // If the image is square, scale it proportionally
                    if (formattedFrame.PixelWidth == formattedFrame.PixelHeight)
                    {
                        if (formattedFrame.PixelWidth == targetWidth)
                        {
                            processedBitmap = formattedFrame;
                        }
                        else
                        {
                            processedBitmap = ScaleProportional(formattedFrame, targetWidth);
                        }
                    }
                    else
                    {
                        // Proportional scaling and padding to square
                        processedBitmap = ScaleAndPadToSquare(formattedFrame, targetWidth);
                    }

                    // Add border if required
                    if (addBorder)
                    {
                        processedBitmap = AddBorder(processedBitmap, borderSize, borderColorHex, blurRadius);
                    }

                    // Save frame as PNG
                    string framePath = Path.Combine(framesDir, $"frame_{i:000}.png");
                    SavePng(processedBitmap, framePath);
                    Console.WriteLine($"Saved frame {i} to {framePath}");
                }
            }

            // Build ffmpeg command; ffmpeg.exe should be in the same folder as the application
            string ffmpegPathForVideo = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            string argumentsStr = $"-y -framerate {fps} -i \"{Path.Combine(framesDir, "frame_%03d.png")}\" -c:v libvpx-vp9 -pix_fmt yuva420p -crf {crf} \"{outputVideo}\"";

            while (true)
            {
                Console.WriteLine($"Running ffmpeg with CRF {crf} to create video...");
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ffmpegPathForVideo,
                    Arguments = argumentsStr,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process proc = Process.Start(psi))
                {
                    string output = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    Console.WriteLine(output);
                }

                FileInfo fileInfo = new FileInfo(outputVideo);
                if (fileInfo.Length <= 256 * 1024)
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
            Console.WriteLine("  -i, --input <file>       Input GIF file or PNG file (for directory of PNGs)");
            Console.WriteLine("  -o, --output <file>      Output WebM file");
            Console.WriteLine("  -c, --crf-step <value>   CRF step value (default: 2)");
            Console.WriteLine("  -b, --border             Add border to frames");
            Console.WriteLine("      --border-size <value> Border size in pixels (default: 2)");
            Console.WriteLine("      --border-color <hex>  Border color in hex (default: #FFFFFF)");
            Console.WriteLine("      --blur <value>        Border blur radius (integer, no default, required value)");
            Console.WriteLine("      --fps <value>         FPS value (default: 10). Autocalculated for gif");
            Console.WriteLine("  -s, --size <value>       Target size in pixels (default: 512, 1:1 aspect ratio)");
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

        // Helper function to calculate proportional scale and pad to square
        static BitmapSource ScaleAndPadToSquare(BitmapSource source, int targetSize)
        {
            int srcWidth = source.PixelWidth;
            int srcHeight = source.PixelHeight;
            double scale = (double)targetSize / Math.Max(srcWidth, srcHeight);
            int scaledWidth = (int)Math.Round(srcWidth * scale);
            int scaledHeight = (int)Math.Round(srcHeight * scale);

            // Scale the image proportionally
            TransformedBitmap scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));

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
