using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using Quaero.Core.Models;

namespace Quaero.UI.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly IndexConfiguration _config;
    private readonly string _googleOAuthSettingsPath;
    private Process? _indexerProcess;
    private bool _isIndexerRunning;
    private string _indexerStatusText = "Stopped";
    private string _indexerOutput = string.Empty;
    private string _googleClientId = string.Empty;
    private string _googleClientSecret = string.Empty;
    private string _googleSignedInEmail = string.Empty;
    private string _googleSignInStatus = "Not signed in";
    private string _googleRefreshToken = string.Empty;
    private bool _isGoogleSignInInProgress;
    private string _serverBaseUrl = "http://localhost:5055";

    public SettingsViewModel(IndexConfiguration config)
    {
        _config = config;
        _serverBaseUrl = string.IsNullOrWhiteSpace(config.ServerBaseUrl) ? "http://localhost:5055" : config.ServerBaseUrl;
        _googleOAuthSettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Quaero",
            "google-oauth.json");
        LoadGoogleOAuthSettings();
    }

    public string ServerBaseUrl
    {
        get => _serverBaseUrl;
        set
        {
            if (SetField(ref _serverBaseUrl, value))
            {
                _config.ServerBaseUrl = string.IsNullOrWhiteSpace(value) ? "http://localhost:5055" : value.Trim();
                SaveConfiguration();
            }
        }
    }

    public string GoogleClientSecret
    {
        get => _googleClientSecret;
        set
        {
            if (SetField(ref _googleClientSecret, value))
                SaveGoogleOAuthSettings();
        }
    }

    public bool IsIndexerRunning
    {
        get => _isIndexerRunning;
        private set
        {
            if (SetField(ref _isIndexerRunning, value))
            {
                OnPropertyChanged(nameof(IsIndexerStopped));
                OnPropertyChanged(nameof(StartStopButtonText));
            }
        }
    }

    public bool IsIndexerStopped => !_isIndexerRunning;
    public string StartStopButtonText => _isIndexerRunning ? "Stop Indexer" : "Start Indexer";

    public string IndexerStatusText
    {
        get => _indexerStatusText;
        private set => SetField(ref _indexerStatusText, value);
    }

    public string IndexerOutput
    {
        get => _indexerOutput;
        private set => SetField(ref _indexerOutput, value);
    }

    public string GoogleClientId
    {
        get => _googleClientId;
        set
        {
            if (SetField(ref _googleClientId, value))
                SaveGoogleOAuthSettings();
        }
    }

    public string GoogleSignedInEmail
    {
        get => _googleSignedInEmail;
        private set => SetField(ref _googleSignedInEmail, value);
    }

    public string GoogleSignInStatus
    {
        get => _googleSignInStatus;
        private set => SetField(ref _googleSignInStatus, value);
    }

    public bool HasGoogleSignIn => !string.IsNullOrWhiteSpace(_googleRefreshToken);

    public bool IsGoogleSignInInProgress
    {
        get => _isGoogleSignInInProgress;
        private set => SetField(ref _isGoogleSignInInProgress, value);
    }

    public string GoogleOAuthSettingsPath => _googleOAuthSettingsPath;
    public string DatabasePath => _config.DatabasePath;
    public string PluginsDirectory => _config.PluginsDirectory;
    public string DataSourcesConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quaero", "datasources.json");

    public string IndexerExePath
    {
        get
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "indexer", "Quaero.Indexer.exe");
            if (File.Exists(bundled)) return bundled;

            var bundledDll = Path.Combine(AppContext.BaseDirectory, "indexer", "Quaero.Indexer.dll");
            if (File.Exists(bundledDll)) return bundledDll;

            var devPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "Quaero.Indexer", "bin", "Debug", "net10.0", "Quaero.Indexer.exe");
            if (File.Exists(devPath)) return Path.GetFullPath(devPath);

            return "Quaero.Indexer";
        }
    }

    public void ToggleIndexer()
    {
        if (IsIndexerRunning)
            StopIndexer();
        else
            StartIndexer();
    }

    public void StartIndexer()
    {
        if (IsIndexerRunning) return;

        try
        {
            var exePath = IndexerExePath;
            var isDll = exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

            var psi = new ProcessStartInfo
            {
                FileName = isDll ? "dotnet" : exePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (isDll)
                psi.ArgumentList.Add(exePath);

            psi.Environment["QUAERO_PLUGINS_DIR"] = _config.PluginsDirectory;
            psi.Environment["QUAERO_SERVER_BASE_URL"] = _config.ServerBaseUrl;

            _indexerProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _indexerProcess.OutputDataReceived += OnOutputReceived;
            _indexerProcess.ErrorDataReceived += OnOutputReceived;
            _indexerProcess.Exited += OnIndexerExited;

            _indexerProcess.Start();
            _indexerProcess.BeginOutputReadLine();
            _indexerProcess.BeginErrorReadLine();

            IsIndexerRunning = true;
            IndexerStatusText = $"Running (PID {_indexerProcess.Id})";
            AppendOutput($"[Indexer started: {exePath}, PID {_indexerProcess.Id}]");
        }
        catch (Exception ex)
        {
            IndexerStatusText = $"Failed to start: {ex.Message}";
            AppendOutput($"[Error starting indexer: {ex.Message}]");
        }
    }

    public void StopIndexer()
    {
        if (_indexerProcess == null || !IsIndexerRunning) return;

        try
        {
            _indexerProcess.Kill(entireProcessTree: true);
            AppendOutput("[Indexer stop requested]");
        }
        catch (Exception ex)
        {
            AppendOutput($"[Error stopping indexer: {ex.Message}]");
        }
    }

    public void Shutdown()
    {
        if (_indexerProcess != null && !_indexerProcess.HasExited)
        {
            try { _indexerProcess.Kill(entireProcessTree: true); } catch { }
        }
    }

    private void OnOutputReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null)
            AppendOutput(e.Data);
    }

    private void OnIndexerExited(object? sender, EventArgs e)
    {
        var exitCode = _indexerProcess?.ExitCode;
        IsIndexerRunning = false;
        IndexerStatusText = $"Stopped (exit code {exitCode})";
        AppendOutput($"[Indexer exited with code {exitCode}]");
    }

    private void AppendOutput(string line)
    {
        var lines = IndexerOutput.Split('\n');
        if (lines.Length > 200)
            IndexerOutput = string.Join('\n', lines[^200..]) + "\n" + line;
        else
            IndexerOutput = IndexerOutput + (IndexerOutput.Length > 0 ? "\n" : "") + line;
    }

    public async Task SignInWithGoogleAsync()
    {
        if (IsGoogleSignInInProgress)
        {
            GoogleSignInStatus = "Google sign-in is already in progress.";
            return;
        }

        if (string.IsNullOrWhiteSpace(GoogleClientId))
        {
            GoogleSignInStatus = "Enter Google OAuth Client ID first.";
            return;
        }

        IsGoogleSignInInProgress = true;
        var state = Guid.NewGuid().ToString("N");
        var codeVerifier = CreatePkceCodeVerifier();
        var codeChallenge = CreatePkceCodeChallenge(codeVerifier);
        var port = GetAvailableLoopbackPort();
        var redirectUri = $"http://127.0.0.1:{port}/oauth2callback/";
        var scope = Uri.EscapeDataString("https://www.googleapis.com/auth/gmail.readonly");
        var authUrl =
            $"https://accounts.google.com/o/oauth2/v2/auth?client_id={Uri.EscapeDataString(GoogleClientId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&scope={scope}&access_type=offline&prompt=consent&state={state}&code_challenge={Uri.EscapeDataString(codeChallenge)}&code_challenge_method=S256";

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);

        try
        {
            listener.Start();
            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
            GoogleSignInStatus = "Waiting for Google sign-in...";

            var contextTask = listener.GetContextAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(2));
            var completed = await Task.WhenAny(contextTask, timeoutTask);

            if (completed != contextTask)
            {
                GoogleSignInStatus = "Google sign-in timed out.";
                return;
            }

            var context = await contextTask;
            var request = context.Request;

            var responseHtml = "<html><body><h2>Quaero Google Sign-in Complete</h2>You can close this window.</body></html>";

            var returnedState = request.QueryString["state"];
            var code = request.QueryString["code"];
            var error = request.QueryString["error"];

            if (!string.IsNullOrWhiteSpace(error))
            {
                GoogleSignInStatus = $"Google sign-in failed: {error}";
                responseHtml = "<html><body><h2>Sign-in failed</h2></body></html>";
                await WriteBrowserResponseAsync(context, responseHtml);
                return;
            }

            if (returnedState != state || string.IsNullOrWhiteSpace(code))
            {
                GoogleSignInStatus = "Google sign-in response was invalid.";
                responseHtml = "<html><body><h2>Invalid sign-in response</h2></body></html>";
                await WriteBrowserResponseAsync(context, responseHtml);
                return;
            }

            var tokenPayload = new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = GoogleClientId,
                ["code_verifier"] = codeVerifier,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code"
            };

            if (!string.IsNullOrWhiteSpace(GoogleClientSecret))
                tokenPayload["client_secret"] = GoogleClientSecret;

            using var http = new HttpClient();
            using var tokenResponse = await http.PostAsync(
                "https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(tokenPayload));

            var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
            if (!tokenResponse.IsSuccessStatusCode)
            {
                GoogleSignInStatus = $"Token exchange failed: {tokenResponse.StatusCode} - {tokenJson}";
                responseHtml = "<html><body><h2>Token exchange failed</h2></body></html>";
                await WriteBrowserResponseAsync(context, responseHtml);
                return;
            }

            using var tokenDoc = JsonDocument.Parse(tokenJson);
            var tokenRoot = tokenDoc.RootElement;
            var refreshToken = tokenRoot.TryGetProperty("refresh_token", out var rt)
                ? rt.GetString() ?? string.Empty
                : _googleRefreshToken;
            var accessToken = tokenRoot.TryGetProperty("access_token", out var at)
                ? at.GetString() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(refreshToken) || string.IsNullOrWhiteSpace(accessToken))
            {
                GoogleSignInStatus = "Google OAuth tokens were missing from the response.";
                responseHtml = "<html><body><h2>Missing OAuth tokens</h2></body></html>";
                await WriteBrowserResponseAsync(context, responseHtml);
                return;
            }

            string email = string.Empty;
            using var userInfoRequest = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v3/userinfo");
            userInfoRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var userInfoResponse = await http.SendAsync(userInfoRequest);
            if (userInfoResponse.IsSuccessStatusCode)
            {
                var userJson = await userInfoResponse.Content.ReadAsStringAsync();
                using var userDoc = JsonDocument.Parse(userJson);
                email = userDoc.RootElement.TryGetProperty("email", out var emailProp)
                    ? emailProp.GetString() ?? string.Empty
                    : string.Empty;
            }

            _googleRefreshToken = refreshToken;
            GoogleSignedInEmail = email;
            GoogleSignInStatus = string.IsNullOrWhiteSpace(email)
                ? "Signed in to Google."
                : $"Signed in as {email}";
            OnPropertyChanged(nameof(HasGoogleSignIn));
            SaveGoogleOAuthSettings();

            await WriteBrowserResponseAsync(context, responseHtml);
        }
        catch (Exception ex)
        {
            GoogleSignInStatus = $"Google sign-in failed: {ex.Message}";
        }
        finally
        {
            if (listener.IsListening)
                listener.Stop();
            IsGoogleSignInInProgress = false;
        }
    }

    private void SaveConfiguration()
    {
        try
        {
            var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quaero", "config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private async Task WriteBrowserResponseAsync(HttpListenerContext context, string html)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.LongLength;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.OutputStream.Close();
    }

    private void LoadGoogleOAuthSettings()
    {
        try
        {
            if (!File.Exists(_googleOAuthSettingsPath))
                return;

            var json = File.ReadAllText(_googleOAuthSettingsPath);
            var settings = JsonSerializer.Deserialize<GoogleOAuthSettingsDocument>(json);
            if (settings == null)
                return;

            _googleClientId = settings.ClientId ?? string.Empty;
            _googleClientSecret = settings.ClientSecret ?? string.Empty;
            _googleSignedInEmail = settings.Email ?? string.Empty;
            _googleRefreshToken = settings.RefreshToken ?? string.Empty;
            _googleSignInStatus = string.IsNullOrWhiteSpace(_googleRefreshToken)
                ? "Not signed in"
                : string.IsNullOrWhiteSpace(_googleSignedInEmail)
                    ? "Google account connected"
                    : $"Signed in as {_googleSignedInEmail}";
            OnPropertyChanged(nameof(HasGoogleSignIn));
        }
        catch
        {
            _googleSignInStatus = "Not signed in";
        }
    }

    private void SaveGoogleOAuthSettings()
    {
        try
        {
            var directory = Path.GetDirectoryName(_googleOAuthSettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var settings = new GoogleOAuthSettingsDocument
            {
                ClientId = _googleClientId,
                ClientSecret = _googleClientSecret,
                Email = _googleSignedInEmail,
                RefreshToken = _googleRefreshToken
            };

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_googleOAuthSettingsPath, json);
        }
        catch
        {
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private static string CreatePkceCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string CreatePkceCodeChallenge(string verifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static int GetAvailableLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

internal class GoogleOAuthSettingsDocument
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Email { get; set; }
    public string? RefreshToken { get; set; }
}
