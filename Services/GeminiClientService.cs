using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using JustSTT.Models;

namespace JustSTT.Services
{
    public class TestKeyResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Detail { get; set; }
    }

    public class GeminiClientService
    {
        private readonly ConfigService _configService;
        private HttpClient _httpClient;

        public GeminiClientService(ConfigService configService)
        {
            _configService = configService;
            _httpClient = CreateHttpClient();
        }

        private HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                UseProxy = true,
                DefaultProxyCredentials = CredentialCache.DefaultCredentials
            };

            if (!string.IsNullOrWhiteSpace(_configService.Settings.CustomProxyUrl))
            {
                try
                {
                    handler.Proxy = new WebProxy(_configService.Settings.CustomProxyUrl);
                }
                catch { }
            }

            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(35)
            };
        }

        public async Task<string> TranscribeAudioAsync(string audioFilePath)
        {
            byte[] audioBytes = await ReadAllBytesWithRetryAsync(audioFilePath);
            return await TranscribeAudioBytesAsync(audioBytes);
        }

        public async Task<string> TranscribeAudioBytesAsync(byte[] audioBytes)
        {
            string apiKey = _configService.Settings.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Gemini API Key is not set. Please configure it in Settings.");
            }

            string base64Audio = Convert.ToBase64String(audioBytes);

            string model = string.IsNullOrWhiteSpace(_configService.Settings.ModelName)
                ? "gemini-3.5-transcribe"
                : _configService.Settings.ModelName;

            string baseUrl = string.IsNullOrWhiteSpace(_configService.Settings.CustomBaseUrl)
                ? "https://generativelanguage.googleapis.com"
                : _configService.Settings.CustomBaseUrl.TrimEnd('/');

            string prompt = BuildPrompt();

            // If gemini-3.5-transcribe, use the official Interactions API
            if (model.Contains("transcribe"))
            {
                return await TranscribeViaInteractionsApiAsync(model, base64Audio, prompt, baseUrl, apiKey);
            }
            else
            {
                return await TranscribeViaGenerateContentAsync(model, base64Audio, prompt, baseUrl, apiKey);
            }
        }

        private async Task<string> TranscribeViaInteractionsApiAsync(string model, string base64Audio, string prompt, string baseUrl, string apiKey)
        {
            var requestBody = new
            {
                model = model,
                input = new object[]
                {
                    new
                    {
                        type = "text",
                        text = prompt
                    },
                    new
                    {
                        type = "audio",
                        data = base64Audio,
                        mime_type = "audio/wav"
                    }
                }
            };

            string jsonPayload = JsonSerializer.Serialize(requestBody);
            string url = $"{baseUrl}/v1beta/interactions?key={apiKey}";

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            using var response = await _httpClient.SendAsync(request);
            string responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                string errorDetail = ExtractErrorMessage(responseJson);

                if (response.StatusCode == HttpStatusCode.Forbidden && errorDetail.Contains("location", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception("Google Gemini API error: User location is not supported. Please enable a VPN.");
                }

                // Fallback to gemini-3.5-transcribe via GenerateContent if Interactions API fails
                return await TranscribeViaGenerateContentAsync("gemini-3.5-transcribe", base64Audio, prompt, baseUrl, apiKey);
            }

            string result = ParseInteractionsResponse(responseJson);
            if (string.IsNullOrWhiteSpace(result))
            {
                return await TranscribeViaGenerateContentAsync("gemini-3.5-transcribe", base64Audio, prompt, baseUrl, apiKey);
            }

            return result;
        }

        private async Task<string> TranscribeViaGenerateContentAsync(string model, string base64Audio, string prompt, string baseUrl, string apiKey)
        {
            var requestBody = new
            {
                contents = new object[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = prompt },
                            new
                            {
                                inlineData = new
                                {
                                    mimeType = "audio/wav",
                                    data = base64Audio
                                }
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.1,
                    maxOutputTokens = 2048
                }
            };

            string jsonPayload = JsonSerializer.Serialize(requestBody);
            string url = $"{baseUrl}/v1beta/models/{model}:generateContent?key={apiKey}";

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            using var response = await _httpClient.SendAsync(request);
            string responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                string errorDetail = ExtractErrorMessage(responseJson);

                if (response.StatusCode == HttpStatusCode.Forbidden && errorDetail.Contains("location", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception("Google Gemini API error: User location is not supported. Please enable a VPN.");
                }

                throw new Exception($"Gemini API error ({response.StatusCode}): {errorDetail}");
            }

            return ParseGenerateContentResponse(responseJson);
        }

        public void RefreshHttpClient()
        {
            try
            {
                var old = _httpClient;
                _httpClient = CreateHttpClient();
                old.Dispose();
            }
            catch { }
        }

        public async Task<TestKeyResult> TestApiKeyAsync(string apiKey, string modelName, string? customBaseUrl = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new TestKeyResult { Success = false, Message = "Please enter an API key." };
            }

            try
            {
                string baseUrl = string.IsNullOrWhiteSpace(customBaseUrl)
                    ? (string.IsNullOrWhiteSpace(_configService.Settings.CustomBaseUrl) ? "https://generativelanguage.googleapis.com" : _configService.Settings.CustomBaseUrl)
                    : customBaseUrl;
                baseUrl = baseUrl.TrimEnd('/');

                string listUrl = $"{baseUrl}/v1beta/models?key={apiKey}";
                using var listRequest = new HttpRequestMessage(HttpMethod.Get, listUrl);

                var response = await _httpClient.SendAsync(listRequest);
                string responseJson = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return new TestKeyResult
                    {
                        Success = true,
                        Message = "✓ Connection successful! Gemini API key is active and valid."
                    };
                }
                else
                {
                    string errorMsg = ExtractErrorMessage(responseJson);

                    if (response.StatusCode == HttpStatusCode.Forbidden && errorMsg.Contains("location", StringComparison.OrdinalIgnoreCase))
                    {
                        return new TestKeyResult
                        {
                            Success = false,
                            Message = "❌ Google error: User location is not supported.",
                            Detail = "Please turn on your VPN (e.g. Amnezia/WireGuard) and try again."
                        };
                    }

                    if (response.StatusCode == HttpStatusCode.BadRequest && (errorMsg.Contains("API_KEY_INVALID") || errorMsg.Contains("key not valid")))
                    {
                        return new TestKeyResult
                        {
                            Success = false,
                            Message = "❌ Invalid API Key.",
                            Detail = "Please verify that you copied the complete API key from https://aistudio.google.com/apikey."
                        };
                    }

                    return new TestKeyResult
                    {
                        Success = false,
                        Message = $"❌ Gemini API Error ({response.StatusCode})",
                        Detail = errorMsg
                    };
                }
            }
            catch (HttpRequestException ex)
            {
                return new TestKeyResult
                {
                    Success = false,
                    Message = "❌ Network connection error.",
                    Detail = $"Could not reach Google servers: {ex.Message}. Check your VPN connection."
                };
            }
            catch (Exception ex)
            {
                return new TestKeyResult
                {
                    Success = false,
                    Message = "❌ Error testing key.",
                    Detail = ex.Message
                };
            }
        }

        private static string ExtractErrorMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error", out var errorEl))
                {
                    if (errorEl.TryGetProperty("message", out var msgEl))
                    {
                        return msgEl.GetString() ?? json;
                    }
                }
            }
            catch { }
            return json;
        }

        private string BuildPrompt()
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are an expert voice dictation transcriber and editor.");
            sb.AppendLine("Task: Accurately transcribe the provided spoken audio into clean, high-quality written text.");
            sb.AppendLine("Rules:");
            sb.AppendLine("1. Remove all filler words, stutters, and hesitations (e.g. 'um', 'uh', 'ah', 'э-э', 'ну', 'типа', 'как бы').");
            sb.AppendLine("2. Resolve any mid-sentence self-corrections smoothly (e.g., 'let's meet at one no make it two o'clock' -> 'Let's meet at 2:00').");
            sb.AppendLine("3. Preserve the exact intended meaning, language (Russian, English, or mixed), and punctuation.");
            sb.AppendLine("4. Output ONLY the transcribed text. Do NOT add any preamble, markdown fences, notes, explanations, or conversational replies.");

            switch (_configService.Settings.TonePreset)
            {
                case "Casual Chat":
                    sb.AppendLine("Style: Casual, punchy, suitable for direct chat / messaging.");
                    break;
                case "Formal Email":
                    sb.AppendLine("Style: Professional, polished, well-structured business style.");
                    break;
                case "Code & Technical":
                    sb.AppendLine("Style: Technical. Keep function names, variables, and code keywords accurately formatted.");
                    break;
                case "Direct":
                    sb.AppendLine("Style: Direct verbatim transcription without extra rephrasing.");
                    break;
                default:
                    sb.AppendLine("Style: Natural, clean, grammatical written text.");
                    break;
            }

            if (!string.IsNullOrWhiteSpace(_configService.Settings.CustomPrompt))
            {
                sb.AppendLine($"Additional user instruction: {_configService.Settings.CustomPrompt}");
            }

            if (_configService.Settings.CustomVocabulary != null && _configService.Settings.CustomVocabulary.Count > 0)
            {
                sb.AppendLine("Specific custom terms, names, and vocabulary to spell accurately:");
                sb.AppendLine(string.Join(", ", _configService.Settings.CustomVocabulary));
            }

            return sb.ToString();
        }

        private static string ParseInteractionsResponse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("steps", out var steps) && steps.GetArrayLength() > 0)
                {
                    var sb = new StringBuilder();
                    foreach (var step in steps.EnumerateArray())
                    {
                        if (step.TryGetProperty("content", out var contentArray))
                        {
                            foreach (var item in contentArray.EnumerateArray())
                            {
                                if (item.TryGetProperty("text", out var textEl))
                                {
                                    sb.Append(textEl.GetString());
                                }
                            }
                        }
                    }

                    return sb.ToString().Trim();
                }
            }
            catch { }

            return string.Empty;
        }

        private static string ParseGenerateContentResponse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                {
                    var firstCandidate = candidates[0];
                    if (firstCandidate.TryGetProperty("content", out var content) &&
                        content.TryGetProperty("parts", out var parts) &&
                        parts.GetArrayLength() > 0)
                    {
                        var textBuilder = new StringBuilder();
                        foreach (var part in parts.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var textEl))
                            {
                                textBuilder.Append(textEl.GetString());
                            }
                        }

                        return textBuilder.ToString().Trim();
                    }
                }
            }
            catch { }

            return string.Empty;
        }

        private static async Task<byte[]> ReadAllBytesWithRetryAsync(string filePath)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var ms = new MemoryStream();
                        await fs.CopyToAsync(ms);
                        return ms.ToArray();
                    }
                }
                catch (IOException)
                {
                    await Task.Delay(100);
                }
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Audio recording file was not found on disk.", filePath);
            }

            using var fallbackFs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var fallbackMs = new MemoryStream();
            await fallbackFs.CopyToAsync(fallbackMs);
            return fallbackMs.ToArray();
        }
    }
}
