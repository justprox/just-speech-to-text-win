using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;

namespace JustSTT.Services
{
    public class AudioCaptureService : IDisposable
    {
        private WaveInEvent? _waveIn;
        private WaveFileWriter? _writer;
        private MemoryStream? _memoryStream;
        private string? _currentFilePath;
        private DateTime _recordingStartTime;
        private bool _isRecording;
        private TaskCompletionSource<bool>? _stopTcs;
        private readonly object _lock = new();

        public bool IsRecording => _isRecording;
        public string? CurrentFilePath => _currentFilePath;
        public byte[]? LastRecordedWavBytes { get; private set; }

        public event Action<float>? AudioLevelUpdated;
        public event Action<byte[]>? RawAudioChunkAvailable;
        public event Action<string>? RecordingFailed;

        public List<string> GetAvailableInputDevices()
        {
            var devices = new List<string> { "Default" };
            for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                var caps = WaveInEvent.GetCapabilities(i);
                devices.Add(caps.ProductName);
            }
            return devices;
        }

        public bool StartRecording(string? targetFilePath = null, string? selectedDeviceName = null)
        {
            lock (_lock)
            {
                if (_isRecording) return false;

                try
                {
                    _currentFilePath = targetFilePath;
                    LastRecordedWavBytes = null;

                    int deviceNumber = 0;
                    if (!string.IsNullOrEmpty(selectedDeviceName) && selectedDeviceName != "Default")
                    {
                        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
                        {
                            if (WaveInEvent.GetCapabilities(i).ProductName == selectedDeviceName)
                            {
                                deviceNumber = i;
                                break;
                            }
                        }
                    }

                    // 16kHz, 16-bit, Mono (standard speech format)
                    var waveFormat = new WaveFormat(16000, 16, 1);
                    _waveIn = new WaveInEvent
                    {
                        DeviceNumber = deviceNumber,
                        WaveFormat = waveFormat,
                        BufferMilliseconds = 40
                    };

                    // Capture directly to MemoryStream for in-memory / zero-disk operation
                    _memoryStream = new MemoryStream();
                    _writer = new WaveFileWriter(_memoryStream, waveFormat);

                    _waveIn.DataAvailable += OnDataAvailable;
                    _waveIn.RecordingStopped += OnRecordingStopped;

                    _waveIn.StartRecording();
                    _recordingStartTime = DateTime.Now;
                    _isRecording = true;
                    return true;
                }
                catch (Exception ex)
                {
                    Cleanup();
                    RecordingFailed?.Invoke(ex.Message);
                    return false;
                }
            }
        }

        public async Task<double> StopRecordingAsync()
        {
            TaskCompletionSource<bool> tcs;
            double duration = 0;

            lock (_lock)
            {
                if (!_isRecording || _waveIn == null)
                {
                    return 0;
                }

                duration = (DateTime.Now - _recordingStartTime).TotalSeconds;
                _isRecording = false;
                _stopTcs = new TaskCompletionSource<bool>();
                tcs = _stopTcs;

                try
                {
                    _waveIn.StopRecording();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error stopping wave in: {ex.Message}");
                    Cleanup();
                    return duration;
                }
            }

            // Wait for NAudio RecordingStopped callback with timeout
            try
            {
                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(1200));
                if (completedTask != tcs.Task)
                {
                    Cleanup();
                }
            }
            catch
            {
                Cleanup();
            }

            return duration;
        }

        public void CancelRecording()
        {
            lock (_lock)
            {
                _isRecording = false;
                if (_waveIn != null)
                {
                    try { _waveIn.StopRecording(); } catch { }
                }
                Cleanup();

                if (!string.IsNullOrEmpty(_currentFilePath) && File.Exists(_currentFilePath))
                {
                    try { File.Delete(_currentFilePath); } catch { }
                }
                _currentFilePath = null;
                LastRecordedWavBytes = null;
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            try
            {
                lock (_lock)
                {
                    if (_writer != null && _isRecording)
                    {
                        _writer.Write(e.Buffer, 0, e.BytesRecorded);
                    }
                }

                // Send chunk to streaming listeners (WebSocket)
                if (RawAudioChunkAvailable != null && _isRecording)
                {
                    byte[] chunk = new byte[e.BytesRecorded];
                    Buffer.BlockCopy(e.Buffer, 0, chunk, 0, e.BytesRecorded);
                    RawAudioChunkAvailable.Invoke(chunk);
                }

                // Calculate RMS level for visualizer
                float maxSample = 0;
                for (int index = 0; index < e.BytesRecorded; index += 2)
                {
                    short sample = (short)((e.Buffer[index + 1] << 8) | e.Buffer[index]);
                    float sample32 = Math.Abs(sample / 32768f);
                    if (sample32 > maxSample)
                    {
                        maxSample = sample32;
                    }
                }

                AudioLevelUpdated?.Invoke(maxSample);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Audio write error: {ex.Message}");
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            lock (_lock)
            {
                _isRecording = false;

                if (_writer != null)
                {
                    try
                    {
                        _writer.Flush();
                        if (_memoryStream != null)
                        {
                            LastRecordedWavBytes = _memoryStream.ToArray();

                            // If disk mode is requested, write bytes to target file
                            if (!string.IsNullOrEmpty(_currentFilePath))
                            {
                                try
                                {
                                    string? dir = Path.GetDirectoryName(_currentFilePath);
                                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                                    {
                                        Directory.CreateDirectory(dir);
                                    }
                                    File.WriteAllBytes(_currentFilePath, LastRecordedWavBytes);
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }

                Cleanup();
                _stopTcs?.TrySetResult(true);
            }

            if (e.Exception != null)
            {
                RecordingFailed?.Invoke(e.Exception.Message);
            }
        }

        private void Cleanup()
        {
            lock (_lock)
            {
                if (_writer != null)
                {
                    try
                    {
                        _writer.Dispose();
                    }
                    catch { }
                    _writer = null;
                }

                if (_memoryStream != null)
                {
                    try
                    {
                        _memoryStream.Dispose();
                    }
                    catch { }
                    _memoryStream = null;
                }

                if (_waveIn != null)
                {
                    try
                    {
                        _waveIn.DataAvailable -= OnDataAvailable;
                        _waveIn.RecordingStopped -= OnRecordingStopped;
                        _waveIn.Dispose();
                    }
                    catch { }
                    _waveIn = null;
                }
            }
        }

        public void Dispose()
        {
            CancelRecording();
            Cleanup();
        }
    }
}
