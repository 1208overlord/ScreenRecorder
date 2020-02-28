// ScreenGun
// - MicrophoneRecorder.cs
// --------------------------------------------------------------------
// Authors: 
// - Jeff Hansen <jeff@jeffijoe.com>
// - Bjarke Søgaard <ekrajb123@gmail.com>
// Copyright (C) ScreenGun Authors 2015. All rights reserved.

using NAudio.Wave;
using System.Diagnostics;

namespace Recorder
{
    /// <summary>
    ///     The microphone recorder.
    /// </summary>
    public class MicrophoneRecorder
    {
        #region Fields

        /// <summary>
        ///     The material folder.
        /// </summary>
        private readonly string outputFilePath;

        /// <summary>
        ///     The wave file.
        /// </summary>
        private WaveFileWriter waveFile;

        /// <summary>
        ///     The wave source.
        /// </summary>
        private WaveInEvent waveSource;

        #endregion

        #region Constructors and Destructors

        /// <summary>
        /// Initializes a new instance of the <see cref="MicrophoneRecorder"/> class.
        /// </summary>
        /// <param name="outputFilePath">
        /// The material folder.
        /// </param>
        public MicrophoneRecorder(string outputFilePath, int deviceNumber)
        {
            DeviceNumber = deviceNumber;
            outputFilePath = outputFilePath;
        }

        /// <summary>
        /// The device number.
        /// </summary>
        public int DeviceNumber { get; private set; }

        #endregion

        #region Public Methods and Operators

        /// <summary>
        ///     The start.
        /// </summary>
        public void Start()
        {
            waveSource = new WaveInEvent
            {
                DeviceNumber = DeviceNumber,
                WaveFormat = new WaveFormat(44100, 2)
            };

            waveFile = new WaveFileWriter(
                outputFilePath, 
                waveSource.WaveFormat);
            waveSource.DataAvailable += OnWaveSourceOnDataAvailable;
            waveSource.StartRecording();
        }

        /// <summary>
        ///     The stop.
        /// </summary>
        public void Stop()
        {
            if (waveSource != null)
            {
                waveSource.StopRecording();
                waveSource.DataAvailable -= OnWaveSourceOnDataAvailable;
                waveSource.Dispose();
                waveSource = null;
            }

            if (waveFile != null)
            {
                waveFile.Dispose();
                waveFile = null;
            }
        }

        public void Pause()
        {
            waveSource.StopRecording();
        }

        #endregion

        #region Methods

        /// <summary>
        /// The on wave source on data available.
        /// </summary>
        /// <param name="sender">
        /// The sender.
        /// </param>
        /// <param name="args">
        /// The args.
        /// </param>
        private void OnWaveSourceOnDataAvailable(object sender, WaveInEventArgs args)
        {
            if (waveFile != null)
            {
                waveFile.Write(args.Buffer, 0, args.BytesRecorded);
                waveFile.Flush();
            }
        }

        #endregion
    }
}