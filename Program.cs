using System;
using System.Diagnostics;
using System.IO;
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
            string inputGif = "input.gif";   // Default input GIF
            string outputVideo = "output.webm"; // Default output file
            int crf = 30; // Default CRF
            int crfStep = 2; // Default CRF step

            // Parse command-line arguments
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-i":
                    case "--input":
                        if (i + 1 < args.Length)
                        {
                            inputGif = args[++i];
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

            // Check if the input file exists
            if (!File.Exists(inputGif))
            {
                Console.WriteLine($"Error: Input file '{inputGif}' not found.");
                PrintHelp();
                return;
            }

            string framesDir = "frames";  // Directory for saving extracted frames

            // Check if the frames directory exists and is not empty
            if (Directory.Exists(framesDir) && Directory.GetFiles(framesDir).Length > 0)
            {
                // Clear the directory
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

            // Create a temporary directory for frames if it doesn't exist
            if (!Directory.Exists(framesDir))
                Directory.CreateDirectory(framesDir);

            // Default target width and height for frames
            int targetWidth = 512;
            int targetHeight = 512;

            // Load GIF using WIC with OnLoad option to load all data immediately
            GifBitmapDecoder decoder;
            using (FileStream fs = new FileStream(inputGif, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                decoder = new GifBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            }

            int frameCount = decoder.Frames.Count;
            Console.WriteLine($"Found {frameCount} frames.");

            // Calculate the total animation time to correctly determine FPS
            double totalDelay = 0;
            for (int i = 0; i < frameCount; i++)
            {
                BitmapFrame frame = decoder.Frames[i];
                double frameDelay = 0.1; // Default value (0.1 sec = 10 FPS) if delay is not specified
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
                        // Delay is stored as ushort, where the value is in hundredths of a second
                        ushort delay = (ushort)delayObj;
                        frameDelay = delay / 100.0;
                    }
                }
                totalDelay += frameDelay;
            }

            // Check GIF duration
            if (totalDelay > 3.0)
            {
                Console.WriteLine("Error: Input GIF duration exceeds 3 seconds.");
                return;
            }

            // If the total time is zero, set the default FPS
            int fps = (int)((totalDelay > 0) ? frameCount / totalDelay : 10);
            Console.WriteLine($"Calculated FPS: {fps}");

            // Variable to store the last valid frame
            BitmapSource lastValidFrame = null;

            // Process each frame: scale and save as PNG
            for (int i = 0; i < frameCount; i++)
            {
                BitmapFrame frame = decoder.Frames[i];

                // Convert frame to format with alpha channel (Bgra32)
                FormatConvertedBitmap formattedFrame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

                // Check if the frame is black
                if (IsBlackFrame(formattedFrame))
                {
                    Console.WriteLine($"Replacing black frame {i} with the last valid frame");
                    if (lastValidFrame != null)
                    {
                        formattedFrame = new FormatConvertedBitmap(lastValidFrame, PixelFormats.Bgra32, null, 0);
                    }
                }
                else
                {
                    lastValidFrame = formattedFrame;
                }

                // Calculate scaling factors
                double scaleX = (double)targetWidth / formattedFrame.PixelWidth;
                double scaleY = (double)targetHeight / formattedFrame.PixelHeight;

                // Scale using TransformedBitmap and ScaleTransform
                TransformedBitmap scaledBitmap = new TransformedBitmap(formattedFrame, new ScaleTransform(scaleX, scaleY));

                // Save frame as PNG file
                string framePath = Path.Combine(framesDir, $"frame_{i:000}.png");
                SavePng(scaledBitmap, framePath);
                Console.WriteLine($"Saved frame {i} to {framePath}");
            }

            // Form the command for ffmpeg
            // It is assumed that ffmpeg.exe is in the same folder as the application
            string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            string arguments = $"-y -framerate {fps} -i \"{Path.Combine(framesDir, "frame_%03d.png")}\" -c:v libvpx-vp9 -pix_fmt yuva420p -crf {crf} \"{outputVideo}\"";

            while (true)
            {
                Console.WriteLine($"Running ffmpeg with CRF {crf} to create video...");
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
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
                arguments = $"-y -framerate {fps} -i \"{Path.Combine(framesDir, "frame_%03d.png")}\" -c:v libvpx-vp9 -pix_fmt yuva420p -crf {crf} \"{outputVideo}\"";
            }

            Console.WriteLine("Video created: " + outputVideo);
            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }

        /// <summary>
        /// Checks if a frame is black.
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
        /// Saves BitmapSource to a PNG file.
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
            Console.WriteLine("  -i, --input <file>       Input GIF file");
            Console.WriteLine("  -o, --output <file>      Output WebM file");
            Console.WriteLine("  -c, --crf-step <value>   CRF step value (default: 2)");
            Console.WriteLine("  -h, --help               Display this help message");
        }
    }
}
