using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JustSTT.Models;

namespace JustSTT.Services
{
    public class GeminiLiveWebSocketService : IDisposable
    {
        private readonly ConfigService _configService;
        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _sessionCts;
        private Task? _receiveLoopTask;
        private readonly object _lock = new();
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        private readonly List<string> _committedTurns = new();
        private string _currentTurnText = string.Empty;

        public event Action<string>? InterimTranscriptReceived;
        public event Action<string>? LiveErrorOccurred;

        public bool IsSessionActive => _webSocket != null && _webSocket.State == WebSocketState.Open;

        public GeminiLiveWebSocketService(ConfigService configService)
        {
            _configService = configService;
        }

        public async Task<bool> StartLiveSessionAsync()
        {
            lock (_lock)
            {
                CancelSession();
                _committedTurns.Clear();
                _currentTurnText = string.Empty;
                _sessionCts = new CancellationTokenSource();
            }

            string apiKey = _configService.Settings.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey)) return false;

            try
            {
                _webSocket = new ClientWebSocket();
                _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

                string baseUrl = string.IsNullOrWhiteSpace(_configService.Settings.CustomBaseUrl)
                    ? "https://generativelanguage.googleapis.com"
                    : _configService.Settings.CustomBaseUrl.TrimEnd('/');

                string wsHost = baseUrl.Replace("https://", "wss://").Replace("http://", "ws://");
                string wsUrl = $"{wsHost}/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent?key={apiKey}";

                using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await _webSocket.ConnectAsync(new Uri(wsUrl), connectCts.Token);

                // Setup Message strictly for gemini-3.5-transcribe-live
                string liveModel = _configService.Settings.ModelName;
                if (!liveModel.StartsWith("models/"))
                {
                    liveModel = $"models/{liveModel}";
                }

                var setup = new
                {
                    setup = new
                    {
                        model = liveModel,
                        generationConfig = new
                        {
                            responseModalities = new[] { "TEXT" }
                        }
                    }
                };

                byte[] setupBytes = JsonSerializer.SerializeToUtf8Bytes(setup);
                
                await _sendLock.WaitAsync();
                try
                {
                    if (_webSocket.State == WebSocketState.Open && _sessionCts != null)
                    {
                        await _webSocket.SendAsync(new ArraySegment<byte>(setupBytes), WebSocketMessageType.Text, true, _sessionCts.Token);
                    }
                }
                finally
                {
                    _sendLock.Release();
                }

                // Start background receive loop
                _receiveLoopTask = Task.Run(ReceiveLoopAsync);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start live WS: {ex.Message}");
                CancelSession();
                LiveErrorOccurred?.Invoke(ex.Message);
                return false;
            }
        }

        public async Task SendAudioChunkAsync(byte[] pcm16kBytes)
        {
            if (_webSocket == null || _webSocket.State != WebSocketState.Open || _sessionCts == null)
            {
                return;
            }

            // Non-blocking try-wait to avoid queuing backlog under network jitter
            if (!await _sendLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                string b64 = Convert.ToBase64String(pcm16kBytes);
                var mediaMsg = new
                {
                    realtimeInput = new
                    {
                        mediaChunks = new object[]
                        {
                            new
                            {
                                mimeType = "audio/pcm;rate=16000",
                                data = b64
                            }
                        }
                    }
                };

                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(mediaMsg);
                if (_webSocket != null && _webSocket.State == WebSocketState.Open && _sessionCts != null)
                {
                    await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _sessionCts.Token);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error streaming audio chunk: {ex.Message}");
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public async Task<string> StopLiveSessionAndGetTranscriptAsync()
        {
            if (_webSocket == null) return string.Empty;

            try
            {
                // Wait briefly for trailing transcription chunks to arrive
                await Task.Delay(400);

                if (_webSocket.State == WebSocketState.Open)
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Complete", closeCts.Token);
                }
            }
            catch { }

            string finalResult = GetFullTranscript();
            CancelSession();
            return finalResult;
        }

        public string GetFullTranscript()
        {
            lock (_lock)
            {
                var list = new List<string>(_committedTurns);
                if (!string.IsNullOrWhiteSpace(_currentTurnText))
                {
                    string trimmed = _currentTurnText.Trim();
                    if (list.Count == 0 || !list[^1].Equals(trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(trimmed);
                    }
                }

                return CleanAndDeduplicate(list);
            }
        }

        public void CancelSession()
        {
            try
            {
                _sessionCts?.Cancel();
                _sessionCts?.Dispose();
                _sessionCts = null;
            }
            catch { }

            if (_webSocket != null)
            {
                try { _webSocket.Dispose(); } catch { }
                _webSocket = null;
            }
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[8192];
            var token = _sessionCts?.Token ?? CancellationToken.None;
            using var ms = new MemoryStream();

            while (_webSocket != null && _webSocket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                try
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult result;

                    // Reassemble multi-frame / fragmented WebSocket messages
                    do
                    {
                        result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            return;
                        }
                        ms.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    ms.Seek(0, SeekOrigin.Begin);
                    using var doc = await JsonDocument.ParseAsync(ms, cancellationToken: token);
                    ParseLiveServerMessage(doc.RootElement);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"WS Receive error: {ex.Message}");
                    break;
                }
            }
        }

        private void ParseLiveServerMessage(JsonElement root)
        {
            try
            {
                if (root.TryGetProperty("serverContent", out var sc))
                {
                    bool turnComplete = false;
                    if (sc.TryGetProperty("turnComplete", out var tc) && tc.GetBoolean())
                    {
                        turnComplete = true;
                    }

                    // 1. Live interim speech recognition
                    if (sc.TryGetProperty("interimInputTranscription", out var interim) &&
                        interim.TryGetProperty("text", out var textEl))
                    {
                        string incoming = textEl.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(incoming))
                        {
                            HandleInterimRecognition(incoming.Trim());
                        }
                    }
                    // 2. Model turn parts
                    else if (sc.TryGetProperty("modelTurn", out var modelTurn) &&
                             modelTurn.TryGetProperty("parts", out var parts))
                    {
                        var sb = new StringBuilder();
                        foreach (var part in parts.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var t))
                            {
                                sb.Append(t.GetString());
                            }
                        }
                        string textChunk = sb.ToString().Trim();
                        if (!string.IsNullOrEmpty(textChunk))
                        {
                            HandleModelTurnChunk(textChunk);
                        }
                    }

                    if (turnComplete)
                    {
                        CommitActiveTurn();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing server message: {ex.Message}");
            }
        }

        private void HandleInterimRecognition(string incoming)
        {
            lock (_lock)
            {
                string cleanIncoming = incoming.Trim();

                // A live utterance stream only grows in length (10 -> 20 -> 35 chars).
                // If text length suddenly drops (e.g. from 35 to 4 chars), Gemini reset its buffer for a new phrase after a pause.
                if (_currentTurnText.Length > 8 && cleanIncoming.Length < _currentTurnText.Length / 2)
                {
                    string prevClean = _currentTurnText.Trim();
                    if (!string.IsNullOrWhiteSpace(prevClean))
                    {
                        if (_committedTurns.Count == 0 || !_committedTurns[^1].Equals(prevClean, StringComparison.OrdinalIgnoreCase))
                        {
                            _committedTurns.Add(prevClean);
                        }
                    }
                }

                _currentTurnText = cleanIncoming;
            }

            string full = GetFullTranscript();
            InterimTranscriptReceived?.Invoke(full);
        }

        private void HandleModelTurnChunk(string textChunk)
        {
            lock (_lock)
            {
                _currentTurnText = textChunk;
            }

            string full = GetFullTranscript();
            InterimTranscriptReceived?.Invoke(full);
        }

        private void CommitActiveTurn()
        {
            lock (_lock)
            {
                if (!string.IsNullOrWhiteSpace(_currentTurnText))
                {
                    string clean = _currentTurnText.Trim();
                    if (_committedTurns.Count == 0 || !_committedTurns[^1].Equals(clean, StringComparison.OrdinalIgnoreCase))
                    {
                        _committedTurns.Add(clean);
                    }
                    _currentTurnText = string.Empty;
                }
            }
        }

        private static string CleanAndDeduplicate(List<string> turns)
        {
            if (turns.Count == 0) return string.Empty;

            var result = new StringBuilder();
            string lastAdded = string.Empty;

            foreach (var rawTurn in turns)
            {
                string turn = rawTurn.Trim();
                if (string.IsNullOrEmpty(turn)) continue;

                if (turn.Equals(lastAdded, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (result.Length > 0 && !result.ToString().EndsWith(" "))
                {
                    result.Append(" ");
                }
                result.Append(turn);
                lastAdded = turn;
            }

            return result.ToString().Trim();
        }

        public void Dispose()
        {
            CancelSession();
            _sendLock.Dispose();
        }
    }
}
