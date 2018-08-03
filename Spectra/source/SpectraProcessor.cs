using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO.Ports;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace Spectra
{
	class SpectraProcessor
	{
        private SerialPort Port;
        private Thread Thread;
        private bool Running;
        private WasapiLoopbackCapture Capture;
        private OpenBufferedWaveProvider WaveProvider;
        private ISampleProvider SampleProvider;


        public SpectraProcessor(SerialPort port)
        {
            Port = port;
        }

        public void Start()
        {
            Running = true;
            Thread = new Thread(Process);
            Thread.Start();
        }

        private void Process()
        {
            Thread.Sleep(100);

            BeginRecording();
            //Setup variables

            while(Running)
            {
                Console.WriteLine("Process here.");
                Thread.Sleep(500);
            }

            StopRecording();
        }

        private void BeginRecording()
        {
            Capture = new WasapiLoopbackCapture();

            Capture.DataAvailable += new EventHandler<WaveInEventArgs>(OnDataAvailable);
            WaveProvider = new OpenBufferedWaveProvider(Capture.WaveFormat);
            WaveProvider.BufferLength = 0; //TODO: Change to const
            WaveProvider.DiscardOnBufferOverflow = false;
            SampleProvider = WaveProvider.ToSampleProvider();

            Capture.StartRecording();
        }

        private void StopRecording()
        {
            Capture.StopRecording();
            Capture.Dispose();
            Capture = null;
            WaveProvider = null;
            SampleProvider = null;
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            WaveProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }

        public void Stop()
        {
            Running = false;
            Thread.Sleep(100);
        }

	}
}
