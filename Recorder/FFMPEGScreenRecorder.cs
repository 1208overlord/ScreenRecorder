// ScreenGun
// - FFMPEGScreenRecorder.cs
// --------------------------------------------------------------------
// Authors: 
// - Jeff Hansen <jeff@jeffijoe.com>
// - Bjarke Søgaard <ekrajb123@gmail.com>
// Copyright (C) ScreenGun Authors 2015. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Recorder
{
    /// <summary>
    ///     An implementation of a screen recorder using FFMPEG.
    /// </summary>
    public class FFMPEGScreenRecorder
    {
        #region Constants

        /// <summary>
        ///     The frame rate.
        /// </summary>
        public const int FrameRate = 20;

        #endregion

        #region Fields

        /// <summary>
        ///     The ffmpeg path.
        /// </summary>
        private readonly string ffmpegPath;

        /// <summary>
        ///     The frame capture backend.
        /// </summary>
        private readonly IFrameCaptureBackend frameCaptureBackend;

        /// <summary>
        ///     The frame saver tasks.
        /// </summary>
        private readonly List<Task> frameSaverTasks;

        /// <summary>
        ///     The frame counter.
        /// </summary>
        private int frameCounter;

        /// <summary>
        ///     The frames.
        /// </summary>
        private ConcurrentQueue<Frame> frames;

        /// <summary>
        /// The saved frames.
        /// </summary>
        private ConcurrentQueue<Frame> savedFrames;

        /// <summary>
        ///     The last frame bitmap.
        /// </summary>
        private Bitmap lastFrameBitmap;

        /// <summary>
        ///     The material folder.
        /// </summary>
        private string materialFolder;

        /// <summary>
        ///     The mic file path.
        /// </summary>
        private string micFilePath;

        /// <summary>
        ///     The mic recorder.
        /// </summary>
        private MicrophoneRecorder micRecorder;

        /// <summary>
        ///     The progress.
        /// </summary>
        private IProgress<RecorderState> progress;

        /// <summary>
        ///     The recorder options.
        /// </summary>
        private ScreenRecorderOptions recorderOptions;

        /// <summary>
        ///     The recording name. Used for creating a material folder.
        /// </summary>
        private string recordingName;

        /// <summary>
        ///     The timer.
        /// </summary>
        private Timer timer;

        /// <summary>
        /// Tracks when the recording was started.
        /// </summary>
        private DateTime recordingStartedAt;

        #endregion

        #region Constructors and Destructors

        /// <summary>
        /// Initializes a new instance of the <see cref="FFMPEGScreenRecorder"/> class.
        /// </summary>
        /// <param name="ffmpegScreenRecorderOptions">
        /// The ffmpeg screen recorder options.
        /// </param>
        public FFMPEGScreenRecorder(FFMPEGScreenRecorderOptions ffmpegScreenRecorderOptions)
        {
            ffmpegPath = Path.GetFullPath(ffmpegScreenRecorderOptions.FfmpegPath).Trim('\\');
            frameCaptureBackend = ffmpegScreenRecorderOptions.FrameCaptureBackend;
            frameSaverTasks = new List<Task>();
            savedFrames = new ConcurrentQueue<Frame>();
        }

        #endregion

        #region Public Properties

        /// <summary>
        ///     Gets a value indicating whether we are recording or not.
        /// </summary>
        public bool IsRecording { get; private set; }

        #endregion

        #region Public Methods and Operators

        /// <summary>
        /// Starts recording. Does not block.
        /// </summary>
        /// <param name="options">
        /// The recorder options.
        /// </param>
        /// <param name="progressReporter">
        /// The progress reporter, if any..
        /// </param>
        public void Start(ScreenRecorderOptions options, IProgress<RecorderState> progressReporter = null)
        {
            if (IsRecording)
            {
                throw new ScreenRecorderException("Already recording.");
            }

            recorderOptions = options;
            progress = progressReporter;
            frameSaverTasks.Clear();
            frames = new ConcurrentQueue<Frame>();
            recordingName = string.Format("Recording {0}", DateTime.Now.ToString("yy-MM-dd HH-mm-ss"));
            materialFolder = Path.Combine(recorderOptions.MaterialTempFolder, recordingName);
            if (Directory.Exists(materialFolder))
            {
                Directory.Delete(micFilePath, true);
            }

            Directory.CreateDirectory(materialFolder);
            micFilePath = Path.Combine(materialFolder, "Microphone.wav");
            recordingStartedAt = DateTime.Now;
            Task.Run((Action)Record);
        }

        /// <summary>
        ///     The stop.
        /// </summary>
        /// <returns>
        ///     The <see cref="Task" />.
        /// </returns>
        public async Task StopAsync(string videoCodec)
        {
            if (IsRecording == false)
            {
                throw new ScreenRecorderException("Not recording.");
            }

            timer.Dispose();
            IsRecording = false;
            if (recorderOptions.RecordMicrophone)
            {
                micRecorder.Stop();
            }

            await Task.Delay(200);
            ReportProgress(new RecorderState(RecordingStage.Encoding));
            await Task.WhenAll(frameSaverTasks.ToArray());
            SaveFrames();
            var inputFilePath = await CreateInputFile();
            await Task.Run(() => Encode(inputFilePath, videoCodec));
            ReportProgress(new RecorderState(RecordingStage.Done));
            if (recorderOptions.DeleteMaterialWhenDone)
            {
                Directory.Delete(materialFolder, true);
            }
        }

        public void Pause()
        {
            timer.Dispose();
            //if(recorderOptions.RecordMicrophone)

        }

        /// <summary>
        /// Generates an input file for FFMPEG
        /// </summary>
        /// <returns></returns>
        private async Task<string> CreateInputFile()
        {
            var path = Path.Combine(materialFolder, "frames.txt");
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            using (var fs = new FileStream(path, FileMode.Append))
            using (var sw = new StreamWriter(fs))
            {
                var arr = savedFrames.ToArray();
                for (var i = 0; i < arr.Length; i++)
                {
                    var frame = arr[i];
                    var next = i + 1;
                    double duration = 1;
                    if (next != arr.Length)
                    {
                        var nextFrame = arr[next];
                        var diff = nextFrame.CapturedAt - frame.CapturedAt;
                        duration = diff.TotalSeconds;
                    }

                    await sw.WriteLineAsync(string.Format("file '{0}'", Path.Combine(materialFolder, frame.FileName)));
                    await sw.WriteLineAsync(string.Format(CultureInfo.InvariantCulture, "duration {0}", duration));
                }
            }

            return path;
        }

        #endregion

        #region Methods

        /// <summary>
        ///     Creates the FFMPEG cmd arguments.
        /// </summary>
        /// <param name="inputFilePath">The input file path.</param>
        /// <returns>
        ///     The <see cref="string" />.
        /// </returns>
        private string CreateFFMPEGArgs(string inputFilePath, string videoCodec)
        {
            /**
             * FFMPEG arg explanation:
             * -f image2 - create a movie from an image sequence.
             * -i "{0}" - input file pattern. In our case, it's the material path + "img000000.png", "img000001.png" and so on.
             * -vf "setpts=1.50*PTS" = vf means video filter, PTS is the amount we are slowing each frame down. Magic number, really.
             * -r {1} - the framerate.
             * -c:v libx264 - video codec, in our case h264.
             * {4} - output file.
             */
            var sb = new StringBuilder();
            sb.AppendFormat(
                "-f concat -i \"{0}\" ", 
                inputFilePath);

            if (recorderOptions.RecordMicrophone)
            {
                sb.AppendFormat("-i \"{0}\" ", micFilePath);
            }

            // Use H.264
            if (videoCodec == "mp4" || videoCodec == "mkv")
                sb.Append("-c:v libx264 ");
            else if (videoCodec == "avi")
                sb.Append("-c:v mjpeg");

            sb.AppendFormat(" -qscale:v 0 ");

            /// Lossless video
            if (videoCodec == "mkv")
                sb.AppendFormat(" -preset ultrafast -crf 0 ");

            // If we recorded the mic, we need to add it as an input.
            if (recorderOptions.RecordMicrophone)
                sb.AppendFormat("-c:a mp3 ");

            // Since we're using YUV-4:2:0, H.264 needs the dimensions to be divisible by 2.
            var width = recorderOptions.RecordingRegion.Width;
            var height = recorderOptions.RecordingRegion.Height;
            if (width % 2 != 0)
            {
                Debug.WriteLine("Adjusting width");
                width--;
            }
            if (height % 2 != 0)
            {
                Debug.WriteLine("Adjusting height");
                height++;
            }

            //sb.AppendFormat("-vf scale={0}:{1} ", width, height);
            sb.AppendFormat(" -s {0}x{1} ", width, height);
            sb.Append("-pix_fmt yuv420p ");
            
            var startFrame = savedFrames.First();
            var endFrame = savedFrames.Last();
            var duration = endFrame.CapturedAt - startFrame.CapturedAt;
            sb.AppendFormat("-t {0} ", duration);
            sb.AppendFormat("\"{0}\" -y", recorderOptions.OutputFilePath);
            return sb.ToString();
        }

        /// <summary>
        ///     Encodes the video.
        /// </summary>
        private void Encode(string inputFilePath, string videoCodec)
        {
            var ffmpegArgs = CreateFFMPEGArgs(inputFilePath, videoCodec);

            // Start a CMD in the background, which itself will run FFMPEG.
            var cmdArgs = string.Format("/C \"\"{0}\" {1}\"", ffmpegPath, ffmpegArgs);
            var startInfo = new ProcessStartInfo("cmd.exe", cmdArgs)
            {
                UseShellExecute = true, 
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };
            Debug.WriteLine(cmdArgs);

            //using (StreamWriter outputFile = new StreamWriter("D:/temp.txt"))
            //{
            //    outputFile.Write(cmdArgs);
            //}
            var process = Process.Start(startInfo);
            process.WaitForExit();
        }

        /// <summary>
        ///     Records this instance.
        /// </summary>
        private void Record()
        {
            IsRecording = true;
            frameCounter = 0;
            RecordFrame();
            timer = new Timer(_ => RecordFrame());
            timer.Change(1000 / FrameRate, 1000 / FrameRate);

            if (recorderOptions.RecordMicrophone)
            {
                micRecorder = new MicrophoneRecorder(micFilePath, recorderOptions.AudioRecordingDeviceNumber);
                micRecorder.Start();
            }

            ReportProgress(new RecorderState(RecordingStage.Recording));
        }

        /// <summary>
        ///     The record frame.
        /// </summary>
        private void RecordFrame()
        {
            if (IsRecording == false)
            {
                return;
            }

            // Capture a frame.
            Bitmap frameBitmap;
            DateTime capturedAt;
            try
            {
                frameBitmap = frameCaptureBackend.CaptureFrame(recorderOptions);
                capturedAt = DateTime.Now;
                lastFrameBitmap = frameBitmap;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                if (lastFrameBitmap != null)
                {
                    frameBitmap = lastFrameBitmap;
                    capturedAt = DateTime.Now;
                }
                else
                {
                    return;
                }
            }

            Interlocked.Increment(ref frameCounter);
            var fileName = string.Format("img{0}.png", frameCounter.ToString("D6"));
            var path = Path.Combine(recorderOptions.MaterialTempFolder, recordingName, fileName);

            var frame = new Frame(fileName, frameCounter, capturedAt, frameBitmap);
            
            frames.Enqueue(frame);
            savedFrames.Enqueue(frame);
            if (frames.Count > 30)
            {
                var task = Task.Run(
                    () => SaveFrames());
                frameSaverTasks.Add(task);
            }
        }

        /// <summary>
        /// Reports the progress.
        /// </summary>
        /// <param name="state">
        /// The state.
        /// </param>
        private void ReportProgress(RecorderState state)
        {
            if (progress != null)
            {
                progress.Report(state);
            }
        }

        /// <summary>
        ///     The save frames.
        /// </summary>
        private void SaveFrames()
        {
            Frame frame;
            while (frames.TryDequeue(out frame))
            {
                if (frame == null)
                {
                    continue;
                }

                var fileName = string.Format("img{0}.png", frame.FrameNumber.ToString("D6"));
                var path = Path.Combine(recorderOptions.MaterialTempFolder, recordingName, fileName);
                try
                {
                    frame.FrameBitmap.Save(path, ImageFormat.Png);
                    frame.FrameBitmap.Dispose();
                }
                catch (Exception ex)
                {
                    Debugger.Break();
                    Console.WriteLine(ex.Message);
                    throw;
                }
            }
        }

        #endregion
    
        /// <summary>
        ///     The frame.
        /// </summary>
        private class Frame
        {
            #region Constructors and Destructors

            /// <summary>
            /// Initializes a new instance of the <see cref="Frame"/> class.
            /// </summary>
            /// <param name="frameNumber">
            /// The frame number.
            /// </param>
            /// <param name="frameBitmap">
            /// The frame bitmap.
            /// </param>
            public Frame(string fileName, int frameNumber, DateTime capturedAt, Bitmap frameBitmap)
            {
                FileName = fileName;
                FrameNumber = frameNumber;
                FrameBitmap = frameBitmap;
                CapturedAt = capturedAt;
            }

            #endregion

            #region Public Properties

            /// <summary>
            /// The file name.
            /// </summary>
            public string FileName { get; private set; }

            /// <summary>
            ///     Gets the frame bitmap.
            /// </summary>
            public Bitmap FrameBitmap { get; private set; }

            /// <summary>
            ///     Gets the frame number.
            /// </summary>
            public int FrameNumber { get; private set; }

            /// <summary>
            /// Timestamp from when the frame was shot.
            /// </summary>
            public DateTime CapturedAt { get; private set; }

            #endregion
        }
    }
}