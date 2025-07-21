using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ToshinoKyouko;

public static partial class Program
{
    private const string TEMP_SUB_FILENAME = "temp.srt";
    private static readonly string[] ToshinoVariations =
    {
        "Toshino Kyouko",
        "Toshino Kyoko",
        "Toshinou Kyouko",
        "Toshinou Kyoko"
    };

    private static readonly string[] AyanoVariations =
    {
        "Sugiura Ayano",
        "Sugiura Ayanou"
    };

    private struct VideoInfo
    {
        public string FilePath = string.Empty;
        public string FileName = string.Empty;
        public TimeSpan Duration = TimeSpan.Zero;
        public float Fps = 0.0f;

        public VideoInfo() { }
    }

    private struct Subtitle
    {
        public string Text = string.Empty;
        public TimeSpan StartTime = TimeSpan.Zero;
        public TimeSpan EndTime = TimeSpan.Zero;

        public Subtitle() { }
    }

    [GeneratedRegex("(\\d+:\\d+:\\d+,\\d+) --> (\\d+:\\d+:\\d+,\\d+)")]
    private static partial Regex TimeRegex();

    private static Process GetNewFFMpegProcess(string args)
    {
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg.exe",
                Arguments = args,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
    }

    private static VideoInfo GetVideoInfo(string filePath)
    {
        // Stream #0:0[0x1](und): Video: h264 (Main) (avc1 / 0x31637661), yuv420p(progressive), 480x360 [SAR 1:1 DAR 4:3], 253 kb/s, 25.08 fps, 25 tbr, 90k tbn (default)
        Console.WriteLine($"Getting information for video in path \"{filePath}\"");
        const string durationMarker = "Duration: ";
        const string fpsMarker = " fps,";
        using Process ffmpegProcess = GetNewFFMpegProcess($"-i \"{filePath}\"");
        ffmpegProcess.Start();
        string output = ffmpegProcess.StandardError.ReadToEnd();
        // Console.WriteLine(output);
        ffmpegProcess.WaitForExit();
        ffmpegProcess.Close();
        int durationStartIndex = output.IndexOf(durationMarker) + durationMarker.Length;
        int durationEndIndex = output.IndexOf(",", durationStartIndex);
        string duration = output.Substring(durationStartIndex, durationEndIndex - durationStartIndex).Trim();
        int fpsEndIndex = output.IndexOf(fpsMarker);

        string fps = string.Empty;
        for (int i = fpsEndIndex - 1; i >= 0; i--)
        {
            if (output[i] == ' ')
                break;
            fps = output[i] + fps;
        }

        string fileName = Path.GetFileName(filePath);
        return new VideoInfo
        {
            FilePath = filePath,
            FileName = fileName,
            Duration = TimeSpan.Parse(duration),
            Fps = float.Parse(fps.Replace('.', ','))
        };
    }

    private static void ExtractSubtitles(VideoInfo info, string outputFile)
    {
        Console.WriteLine($"[{info.FileName}] Started extracting subtitles");
        using Process ffmpegProcess = GetNewFFMpegProcess($"-i \"{info.FilePath}\" -c:s srt -y \"{outputFile}\"");
        ffmpegProcess.EnableRaisingEvents = true;
        ffmpegProcess.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data) && e.Data.StartsWith("size="))
                Console.WriteLine($"[{info.FileName}] Progress: {e.Data}");
        };
        ffmpegProcess.Start();
        ffmpegProcess.BeginErrorReadLine();
        ffmpegProcess.WaitForExit();
        ffmpegProcess.Close();
        Console.WriteLine($"[{info.FileName}] Finished extracting subtitles");
    }

    private static Subtitle[] ParseSubtitle(VideoInfo info, string filepath)
    {
        Console.WriteLine($"[{info.FileName}] Started parsing subtitles");
        List<Subtitle> subtitles = new();
        string[] lines = File.ReadAllLines(filepath);
        Subtitle currentSubtitle = default;
        Regex timeRegex = TimeRegex();

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) && !currentSubtitle.Equals(default(Subtitle)))
            {
                subtitles.Add(currentSubtitle);
                currentSubtitle = default;
            }
            else if (timeRegex.IsMatch(line))
            {
                Match match = timeRegex.Match(line);
                currentSubtitle = new Subtitle
                {
                    StartTime = TimeSpan.Parse(match.Groups[1].Value.Replace(',', '.')),
                    EndTime = TimeSpan.Parse(match.Groups[2].Value.Replace(',', '.'))
                };
            }
            else if (!currentSubtitle.Equals(default(Subtitle)))
            {
                currentSubtitle.Text += line + " ";
            }
        }

        if (!currentSubtitle.Equals(default(Subtitle)))
            subtitles.Add(currentSubtitle);
        Console.WriteLine($"[{info.FileName}] Finished parsing subtitles. Final count is {subtitles.Count}");
        return subtitles.ToArray();
    }

    private static void TrimVideo(VideoInfo info, string outputFileName, TimeSpan start, TimeSpan end)
    {
        Console.WriteLine($"[{info.FileName}] Started trimming video to output path \"{outputFileName}\"");
        const string timeFormat = "hh\\:mm\\:ss\\.fff";
        string startTime = start.ToString(timeFormat);
        string endTime = end.ToString(timeFormat);
        string fps = info.Fps.ToString().Replace(',', '.');
        using Process ffmpegProcess = GetNewFFMpegProcess(
            $"-i \"{info.FilePath}\" -ss \"{startTime}\" -to \"{endTime}\" -vf \"fps={fps}\" " +
            $"-af \"asetpts=PTS-STARTPTS\" -y \"{outputFileName}\"");
        ffmpegProcess.EnableRaisingEvents = true;
        ffmpegProcess.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data) && e.Data.StartsWith("frame="))
                Console.WriteLine($"[{info.FileName}] Progress: {e.Data}".TrimEnd());
        };
        ffmpegProcess.Start();
        ffmpegProcess.BeginErrorReadLine();
        ffmpegProcess.WaitForExit();
        ffmpegProcess.Close();
        Console.WriteLine($"[{info.FileName}] Finished trimming video");
    }

    private static KeyValuePair<TimeSpan, TimeSpan>[] GetToshinoKyoukos(VideoInfo info, Subtitle[] subtitles)
    {
        Console.WriteLine($"[{info.FileName}] Searching for TOSHINO KYOUKOS");
        List<KeyValuePair<TimeSpan, TimeSpan>> kyoukos = new();

        foreach (Subtitle subtitle in subtitles)
        {
            // for (int i = 0; i < ToshinoVariations.Length; i++)
            // {
            //     if (subtitle.Text.Contains(ToshinoVariations[i]))
            //     {
            //         kyoukos.Add(new KeyValuePair<TimeSpan, TimeSpan>(subtitle.StartTime, subtitle.EndTime));
            //         break;
            //     }
            // }
            for (int i = 0; i < AyanoVariations.Length; i++)
            {
                if (subtitle.Text.Contains(AyanoVariations[i]))
                {
                    kyoukos.Add(new KeyValuePair<TimeSpan, TimeSpan>(subtitle.StartTime, subtitle.EndTime));
                    break;
                }
            }
        }

        Console.WriteLine($"[{info.FileName}] Finished for TOSHINO KYOUKOS. Final count is {kyoukos.Count}");
        return kyoukos.ToArray();
    }

    public static void Main(string[] args)
    {
        try
        {
            if (args.Length < 2)
            {
                Console.WriteLine($"Not enough arguments ({args.Length}/2)");
                return;
            }

            string inputDirectory = args[0].Replace('\\', '/');
            string outputDirectory = args[1].Replace('\\', '/');

            if (inputDirectory.EndsWith("/"))
                inputDirectory = inputDirectory.Remove(inputDirectory.Length - 1, 1);
            if (outputDirectory.EndsWith("/"))
                outputDirectory = outputDirectory.Remove(outputDirectory.Length - 1, 1);

            if (!Path.Exists(inputDirectory))
            {
                Console.WriteLine("Input path does not exist");
                return;
            }

            if (!File.GetAttributes(inputDirectory).HasFlag(FileAttributes.Directory))
            {
                Console.WriteLine("Input path is not a directory");
                return;
            }

            if (!Path.Exists(outputDirectory))
            {
                Console.WriteLine("Output path does not exist");
                return;
            }

            if (!File.GetAttributes(outputDirectory).HasFlag(FileAttributes.Directory))
            {
                Console.WriteLine("Output path is not a directory");
                return;
            }

            string[] directoryFiles = Directory.GetFiles(inputDirectory);
            string tempSubFilePath = $"{outputDirectory}/{TEMP_SUB_FILENAME}";
            bool hasEndOffset = false;
            float endOffset = 0.0f;
            if (args.Length == 3)
            {
                hasEndOffset = true;
                endOffset = float.Parse(args[2].Replace('.', ','));
            }

            List<string> videos = new();

            foreach (string directoryFile in directoryFiles)
            {
                if (directoryFile.EndsWith(".mp4") || directoryFile.EndsWith(".mkv"))
                    videos.Add(directoryFile.Replace('\\', '/'));
            }

            videos.Sort();

            foreach (string video in videos)
            {
                Console.WriteLine();
                VideoInfo info = GetVideoInfo(video);
                Console.WriteLine($"File Name: {info.FileName}, Video Duration: {info.Duration.ToString("hh\\:mm\\:ss\\.fff")}, FPS: {info.Fps}");
                ExtractSubtitles(info, tempSubFilePath);
                Subtitle[] subtitles = ParseSubtitle(info, tempSubFilePath);
                File.Delete(tempSubFilePath);
                KeyValuePair<TimeSpan, TimeSpan>[] toshinos = GetToshinoKyoukos(info, subtitles);

                if (toshinos.Length == 0)
                {
                    Console.WriteLine($"[{info.FileName}] Ayano didn't said TOSHINO KYOUKO! in this episode :(");
                    continue;
                }

                for (int i = 0; i < toshinos.Length; i++)
                {
                    (TimeSpan start, TimeSpan end) = toshinos[i];
                    if (hasEndOffset)
                        end = end.Add(TimeSpan.FromSeconds(endOffset));
                    TrimVideo(info, $"{outputDirectory}/({Path.GetFileNameWithoutExtension(video)})_TOSHINO_KYOUKO_{i}.mp4", start, end);
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}