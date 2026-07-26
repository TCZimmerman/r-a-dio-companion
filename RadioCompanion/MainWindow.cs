using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;
using LibVLCSharp.Shared;

namespace RadioCompanion;

public sealed class MainWindow : Window
{
    private const string SseUrl = "https://r-a-d.io/v1/sse?theme=default-dark";
    private const string StreamUrl = "https://relay1.r-a-d.io/main.mp3";
    private const int HotkeyPlayPause = 0x5101;
    private const int HotkeyStop = 0x5102;
    private const int VkMediaPlayPause = 0xB3;
    private const int VkMediaStop = 0xB2;
    private const int WmHotkey = 0x0312;

    private readonly AppSettings _settings = SettingsStore.Load();
    private readonly SseClient _sse = new(SseUrl);
    private readonly HttpClient _imageHttp = new();
    private readonly LibVLC _libVlc;
    private readonly LibVLCSharp.Shared.MediaPlayer _player;
    private Media? _currentMedia;
    private readonly DispatcherTimer _progressTimer;

    private readonly Border _shell = new();
    private readonly TextBlock _djName = new();
    private readonly Border _statusBadge = new();
    private readonly TextBlock _statusText = new();
    private readonly TextBlock _track = new();
    private readonly TextBlock _tags = new();
    private readonly System.Windows.Controls.ProgressBar _progress = new();
    private readonly TextBlock _time = new();
    private readonly TextBlock _connection = new();
    private readonly System.Windows.Controls.Button _playButton = new();
    private readonly Slider _volume = new();
    private readonly Expander _lastExpander = new();
    private readonly Expander _nextExpander = new();
    private readonly StackPanel _lastList = new();
    private readonly StackPanel _nextList = new();
    private readonly WinForms.PictureBox _avatar = new();

    private readonly Popup _menuPopup = new();
    private readonly StackPanel _menuPanel = new();
    private readonly Border _menuBorder = new();

    private readonly Popup _themePopup = new();
    private readonly StackPanel _themePanel = new();
    private Border? _themeMenuItem;

    private MetadataParser.CurrentTrack? _current;
    private MetadataParser.Streamer? _streamer;
    private IReadOnlyList<TrackItem> _queue = Array.Empty<TrackItem>();
    private IReadOnlyList<TrackItem> _lastPlayed = Array.Empty<TrackItem>();
    private long _serverClockOffsetMs;
    private bool _playing;
    private string? _avatarUrl;
    private MemoryStream? _avatarStream;
    private Drawing.Image? _avatarImage;
    private CancellationTokenSource? _avatarLoad;
    private bool _allowClose;
    private bool _menuButtonClosing;
    private bool _connected;

    public MainWindow()
    {
        Title = "R/a/dio Companion";
        Width = 440;
        SizeToContent = System.Windows.SizeToContent.Height;
        MinHeight = 260;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = System.Windows.Media.Brushes.Transparent;
        AllowsTransparency = false;
        Topmost = _settings.AlwaysOnTop;
        WindowStartupLocation = WindowStartupLocation.Manual;

        if (!double.IsNaN(_settings.Left) && !double.IsNaN(_settings.Top))
        {
            Left = _settings.Left;
            Top = _settings.Top;
        }
        else
        {
            Left = SystemParameters.WorkArea.Right - Width - 24;
            Top = SystemParameters.WorkArea.Top + 24;
        }

        
        Core.Initialize();
        _libVlc = new LibVLC();
        _player = new LibVLCSharp.Shared.MediaPlayer(_libVlc);

        Content = BuildUi();
        _menuPopup.Closed += (_, _) =>
        {
            _menuPanel.Children.Clear();

            if (_menuButtonClosing)
            {
                _menuButtonClosing = false;
            }
        };
        PreviewMouseDown += (_, e) =>
        {
            if (_menuPopup.IsOpen &&
                !_menuPopup.IsMouseOver &&
                !_themePopup.IsMouseOver)
            {
                _menuPopup.IsOpen = false;
                _themePopup.IsOpen = false;
            }
        };
        _themePopup.Closed += (_, _) =>
        {
            _themePanel.Children.Clear();
        };

        Deactivated += (_, _) =>
        {
            _menuPopup.IsOpen = false;
            _themePopup.IsOpen = false;
        };
        ApplyTheme(_settings.Theme);

        _volume.Minimum = 0;
        _volume.Maximum = 1;
        _volume.Value = Math.Clamp(_settings.Volume, 0, 1);
        _player.Volume = (int)(_volume.Value * 100);
        _volume.ValueChanged += (_, _) =>
        {
            _player.Volume = (int)(_volume.Value * 100);
            _settings.Volume = _volume.Value;
            SaveSettings();
        };

        _progressTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => UpdateProgress(), Dispatcher);
        _progressTimer.Start();

        _sse.EventReceived += OnSseEvent;

        _sse.ConnectionChanged += connected => Dispatcher.BeginInvoke(() =>
        {
            _connected = connected;
            _connection.Text = connected ? "● connected" : "○ reconnecting…";

            _connection.Foreground = new SolidColorBrush(
                connected
                    ? System.Windows.Media.Color.FromRgb(127, 191, 127)
                    : System.Windows.Media.Color.FromRgb(180, 150, 90));
        });

        Loaded += (_, _) => _sse.Start();
        SourceInitialized += OnSourceInitialized;
        LocationChanged += (_, _) => SavePosition();
        Closing += async (_, e) =>
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                return;
            }
            StopAudio();
            SavePosition();
            await _sse.DisposeAsync();
            DisposeAvatar();
            _currentMedia?.Dispose();
            _player.Dispose();
            _libVlc.Dispose();
            _imageHttp.Dispose();
        };
    }

    private UIElement BuildUi()
    {
        _shell.CornerRadius = new CornerRadius(14);
        _shell.BorderThickness = new Thickness(1);
        _shell.Padding = new Thickness(16);
        _shell.MouseLeftButtonDown += (_, e) =>
        {
            if (!_settings.LockPosition &&
                e.OriginalSource == _shell &&
                e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        };

        var root = new StackPanel();
        _shell.Child = root;

        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _avatar.SizeMode = WinForms.PictureBoxSizeMode.Zoom;
        _avatar.BackColor = Drawing.Color.Transparent;
        var avatarHost = new WindowsFormsHost { Width = 64, Height = 64, Child = _avatar, Margin = new Thickness(0, 0, 10, 0) };
        Grid.SetColumn(avatarHost, 0);
        header.Children.Add(avatarHost);

        var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var djLine = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        _djName.FontSize = 17;
        _djName.FontWeight = FontWeights.SemiBold;
        _djName.Text = "Connecting…";
        _statusBadge.CornerRadius = new CornerRadius(8);
        _statusBadge.Padding = new Thickness(7, 1, 7, 1);
        _statusBadge.Margin = new Thickness(8, 2, 0, 0);
        _statusBadge.VerticalAlignment = VerticalAlignment.Bottom;
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        _statusBadge.Child = _statusText;
        _statusText.FontSize = 10;
        _statusText.FontWeight = FontWeights.Bold;
        djLine.Children.Add(_djName);
        djLine.Children.Add(_statusBadge);
        identity.Children.Add(djLine);
        _connection.FontSize = 10;
        _connection.Margin = new Thickness(0, 5, 0, 0);
        _connection.Text = "○ connecting…";
        identity.Children.Add(_connection);
        Grid.SetColumn(identity, 1);
        header.Children.Add(identity);

        var menuButton = new System.Windows.Controls.Button
        {
            Content = "⋮",
            Width = 32,
            Height = 32,
            FontSize = 20,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Top
        };

        menuButton.Click += (_, _) =>
        {
            if (_menuPopup.IsOpen)
            {
                _menuPopup.IsOpen = false;
                return;
            }

            ShowPopupMenu(menuButton);
        };
        Grid.SetColumn(menuButton, 2);
        header.Children.Add(menuButton);
        root.Children.Add(header);

        _track.FontSize = 19;
        _track.Background = System.Windows.Media.Brushes.Transparent;
        _track.FontWeight = FontWeights.SemiBold;
        _track.TextAlignment = TextAlignment.Center;
        _track.TextWrapping = TextWrapping.Wrap;
        _track.Cursor = System.Windows.Input.Cursors.Hand;
        _track.ToolTip = "Click to copy artist and title";
        _track.MouseLeftButtonUp += (_, _) => CopyCurrent();
        root.Children.Add(_track);

        _tags.FontSize = 12;
        _tags.Background = System.Windows.Media.Brushes.Transparent;
        _tags.TextAlignment = TextAlignment.Center;
        _tags.TextWrapping = TextWrapping.Wrap;
        _tags.MaxHeight = 38;
        _tags.Margin = new Thickness(8, 4, 8, 11);
        _tags.Cursor = System.Windows.Input.Cursors.Hand;
        _tags.ToolTip = "Click to search Source / tags on Google";
        _tags.MouseLeftButtonUp += (_, _) => SearchTags();
        root.Children.Add(_tags);

        _progress.Height = 8;
        _progress.Minimum = 0;
        _progress.Maximum = 1;
        _progress.Cursor = System.Windows.Input.Cursors.Hand;
        _progress.ToolTip = "Click to copy song name";
        _progress.MouseLeftButtonUp += (_, _) => CopyCurrent();
        root.Children.Add(_progress);

        _time.TextAlignment = TextAlignment.Right;
        _time.FontSize = 11;
        _time.Margin = new Thickness(0, 4, 0, 9);
        root.Children.Add(_time);

        ConfigureExpander(_lastExpander, _lastList, "LAST");
        ConfigureExpander(_nextExpander, _nextList, "NEXT");
        _lastExpander.Expanded += (_, _) =>
        {
            if (_nextExpander.IsExpanded)
                _nextExpander.IsExpanded = false;

            RefreshLists();
        };

        _lastExpander.Collapsed += (_, _) => RefreshLists();

        _nextExpander.Expanded += (_, _) =>
        {
            if (_lastExpander.IsExpanded)
                _lastExpander.IsExpanded = false;

            RefreshLists();
        };

        _nextExpander.Collapsed += (_, _) => RefreshLists();
        root.Children.Add(_lastExpander);
        root.Children.Add(_nextExpander);

        var controls = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        controls.ColumnDefinitions.Add(new ColumnDefinition());
        _playButton.Content = "▶  Play";
        _playButton.Height = 36;
        _playButton.Click += (_, _) => ToggleAudio();
        controls.Children.Add(_playButton);

        var volumePanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0)
        };

        volumePanel.Children.Add(new TextBlock
        {
            Text = "🔊",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        _volume.VerticalAlignment = VerticalAlignment.Center;
        _volume.Width = 225;
        volumePanel.Children.Add(_volume);

        Grid.SetColumn(volumePanel, 1);
        controls.Children.Add(volumePanel);
        root.Children.Add(controls);

        return _shell;
    }

    private void ConfigureExpander(Expander expander, StackPanel list, string label)
    {
        expander.Margin = new Thickness(0, 2, 0, 0);
        expander.Content = list;
        expander.Header = new TextBlock
        {
            Text = label,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Width = 365
        };
    }

    private void ShowPopupMenu(System.Windows.Controls.Button menuButton)
    {
        _menuPanel.Children.Clear();
        _menuPanel.Width = 165;

        if (_menuPopup.Child == null)
        {
            _menuBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 37, 37));
            _menuBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(85, 85, 85));
            _menuBorder.BorderThickness = new Thickness(1);
            _menuBorder.CornerRadius = new CornerRadius(8);
            _menuBorder.Padding = new Thickness(6);
            _menuBorder.Child = _menuPanel;

            _menuPopup.Child = _menuBorder;
        }
        _menuPopup.AllowsTransparency = true;
        _menuPopup.Placement = PlacementMode.Bottom;
        _menuPopup.HorizontalOffset = -150;
        _menuPopup.VerticalOffset = 4;
        _menuPopup.StaysOpen = true;

        AddPopupMenuItem(
        "Always on top",
        Topmost,
        () =>
        {
            Topmost = !Topmost;
            _settings.AlwaysOnTop = Topmost;
            SaveSettings();
        });

        AddPopupMenuItem(
            "Lock position",
            _settings.LockPosition,
            () =>
            {
                _settings.LockPosition = !_settings.LockPosition;
                SaveSettings();
            });

        AddPopupMenuItem(
            "Start with Windows",
            _settings.StartWithWindows,
            () =>
            {
                try
                {
                    _settings.StartWithWindows = !_settings.StartWithWindows;
                    SetStartWithWindows(_settings.StartWithWindows);
                    SaveSettings();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        this,
                        ex.Message,
                        "Could not change startup setting",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            });

        _themeMenuItem = AddPopupMenuItem(
            "Theme                 >",
            false,
            () => ShowThemePopup());

        _menuPanel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(85, 85, 85)),
            Margin = new Thickness(0, 5, 0, 5)
        });

        AddPopupMenuItem(
            "Open r/a/dio",
            false,
            () => OpenUrl("https://r-a-d.io/"));

        AddPopupMenuItem(
            "Exit",
            false,
            () =>
            {
                _allowClose = true;
                Close();
            });

        _menuPopup.PlacementTarget = menuButton;
        _menuPopup.IsOpen = true;
    }

private void ShowThemePopup()
    {
        _themePanel.Children.Clear();

        if (_themePopup.Child == null)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 37, 37)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(85, 85, 85)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6),
                Child = _themePanel
            };

            _themePopup.Child = border;
            _themePopup.AllowsTransparency = true;
            _themePopup.StaysOpen = true;
        }

        AddThemeItem("Classic");
        AddThemeItem("Blue");
        AddThemeItem("Light");

        _themePopup.PlacementTarget = _themeMenuItem;
        _themePopup.Placement = PlacementMode.Right;
        _themePopup.HorizontalOffset = 2;
        _themePopup.VerticalOffset = 0;
        _themePopup.IsOpen = true;
    }


    private void AddThemeItem(string theme)
    {
        var selected = theme == _settings.Theme;

        var item = new TextBlock
        {
            Text = selected ? $"✓  {theme}" : $"    {theme}",
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 13,
            Margin = new Thickness(6, 3, 6, 3)
        };

        var border = new Border
        {
            CornerRadius = new CornerRadius(4),
            Child = item
        };

        border.MouseEnter += (_, _) =>
        {
            border.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(58, 58, 58));
        };

        border.MouseLeave += (_, _) =>
        {
            border.Background = null;
        };

        border.MouseLeftButtonUp += (_, _) =>
        {
            _settings.Theme = theme;
            ApplyTheme(theme);
            SaveSettings();

            _themePopup.IsOpen = false;
            _menuPopup.IsOpen = false;
        };

        _themePanel.Children.Add(border);
    }

    private Border AddPopupMenuItem(string text, bool checkedState, Action action)
    {
        var item = new TextBlock
        {
            Text = checkedState ? $"✓  {text}" : $"     {text}",
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 13,
            Margin = new Thickness(6, 3, 6, 3)
        };

        var border = new Border
        {
            CornerRadius = new CornerRadius(4),
            Child = item
        };

        border.MouseEnter += (_, _) =>
        {
            border.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(58, 58, 58));
        };

        border.MouseLeave += (_, _) =>
        {
            border.Background = null;
        };

        border.MouseLeftButtonUp += (_, _) =>
        {
            action();

            if (!text.StartsWith("Theme"))
            {
                _menuPopup.IsOpen = false;
            }
        };

        _menuPanel.Children.Add(border);

        return border;
    }

    private void OnSseEvent(string type, string data)
    {
        Dispatcher.BeginInvoke(() =>
        {
            switch (type.ToLowerInvariant())
            {
                case "time":
                    if (long.TryParse(data.Trim(), out var serverMs))
                        _serverClockOffsetMs = serverMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    break;
                case "metadata":
                    var current = MetadataParser.ParseMetadata(data);
                    if (current is not null)
                    {
                        _current = current;
                        _track.Text = current.Title;
                        _track.ToolTip = current.Title;
                        _tags.Text = string.IsNullOrWhiteSpace(current.Tags) ? string.Empty : $"Source / tags: {current.Tags}";
                        _tags.ToolTip = string.IsNullOrWhiteSpace(current.Tags) ? null : current.Tags;
                        RefreshLists();
                    }
                    break;
                case "streamer":
                    var streamer = MetadataParser.ParseStreamer(data);
                    if (streamer is not null)
                    {
                        _streamer = streamer;
                        _djName.Text = streamer.Name;
                        var isBot = string.Equals(streamer.Name.Trim(), "Hanyuu-sama", StringComparison.OrdinalIgnoreCase);
                        _statusText.Text = isBot ? "● BOT" : "● LIVE";
                        ApplyBadge(isBot);
                        _ = LoadAvatarAsync(streamer.ImagePath);
                    }
                    break;
                case "queue":
                    _queue = MetadataParser.ParseQueue(data);
                    RefreshLists();
                    break;
                case "lastplayed":
                    _lastPlayed = MetadataParser.ParseLastPlayed(data);
                    RefreshLists();
                    break;
            }
        });
    }

    private void RefreshLists()
    {
        var currentTitle = _current?.Title?.Trim();
        var next = _queue.Where(x => !string.Equals(x.Title.Trim(), currentTitle, StringComparison.OrdinalIgnoreCase)).Take(5).ToList();
        var last = _lastPlayed.Take(5).ToList();

        SetExpander(_lastExpander, _lastList, "LAST", last);
        SetExpander(_nextExpander, _nextList, "NEXT", next);
    }

    private void SetExpander(Expander expander, StackPanel panel, string label, IReadOnlyList<TrackItem> items)
    {
        var firstItem = items.FirstOrDefault();
        var first = firstItem is null
            ? "—"
            : $"{firstItem.Title}{(firstItem.IsRequest ? " [REQUEST]" : string.Empty)}";
        if (expander.Header is TextBlock header)
        {
            header.Text = expander.IsExpanded
                ? label
                : $"{label}   {first}";

            header.ToolTip = first;
        }
        panel.Children.Clear();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var row = new TextBlock
            {
                Text = $"{i + 1}.  {item.Title}{(item.IsRequest ? " [REQUEST]" : string.Empty)}",
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(17, 4, 4, 4),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = item.Title
            };
            row.MouseLeftButtonUp += (_, _) => CopyText(item.Title);
            panel.Children.Add(row);
        }
    }

    private void UpdateProgress()
    {
        if (_current is null || _current.StartMs <= 0 || _current.DurationSeconds <= 0)
        {
            _progress.Value = 0;
            _time.Text = $"00:00 / {_current?.DurationText ?? "00:00"}";
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _serverClockOffsetMs;
        var elapsed = Math.Clamp((now - _current.StartMs) / 1000.0, 0, _current.DurationSeconds);
        _progress.Maximum = _current.DurationSeconds;
        _progress.Value = elapsed;
        _time.Text = $"{FormatTime(elapsed)} / {_current.DurationText}";
    }

    private static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
    }

    private void ToggleAudio()
    {
        if (_playing) StopAudio(); else StartAudio();
    }

    private void StartAudio()
    {
        try
        {
            _currentMedia?.Dispose();

            _currentMedia = new Media(_libVlc, StreamUrl, FromType.FromLocation);

            _player.Volume = (int)(_volume.Value * 100);
            var result = _player.Play(_currentMedia);

            if (!result)
            {
                throw new Exception("LibVLC failed to start playback.");
            }

            _playing = true;
            _playButton.Content = "■  Stop";
        }
        catch (Exception ex)
        {
            StopAudio();
            System.Windows.MessageBox.Show(this, ex.Message, "Could not start stream", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StopAudio()
    {
        try { _player.Stop(); } catch { }

        _playing = false;
        _playButton.Content = "▶ Play";
    }

    private async Task LoadAvatarAsync(string path)
    {
        var absolute = new Uri(new Uri("https://r-a-d.io/"), path).ToString();
        if (absolute == _avatarUrl) return;
        _avatarUrl = absolute;
        _avatarLoad?.Cancel();
        _avatarLoad?.Dispose();
        _avatarLoad = new CancellationTokenSource();

        try
        {
            var bytes = await _imageHttp.GetByteArrayAsync(absolute, _avatarLoad.Token);
            await Dispatcher.InvokeAsync(() =>
            {
                DisposeAvatar();
                _avatarStream = new MemoryStream(bytes, writable: false);
                _avatarImage = Drawing.Image.FromStream(_avatarStream, useEmbeddedColorManagement: true, validateImageData: true);
                _avatar.Image = _avatarImage;
            });
        }
        catch (OperationCanceledException) { }
        catch
        {
            // Keep the previous image if the new one fails.
        }
    }

    private void DisposeAvatar()
    {
        _avatar.Image = null;
        _avatarImage?.Dispose();
        _avatarStream?.Dispose();
        _avatarImage = null;
        _avatarStream = null;
    }

    private void CopyCurrent()
    {
        if (!string.IsNullOrWhiteSpace(_current?.Title)) CopyText(_current.Title);
    }

    private void CopyText(string text)
    {
        System.Windows.Clipboard.SetText(text);

        var old = _connection.Text;
        var oldBrush = _connection.Foreground;

        _connection.Text = "✓ Copied to clipboard";
        _connection.Foreground = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(180, 180, 180));

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _connection.Text = old;
            _connection.Foreground = oldBrush;
        };

        timer.Start();
    }

    private void SearchTags()
    {
        if (string.IsNullOrWhiteSpace(_current?.Tags)) return;

        var query = _current.Tags.Split(',', 2)[0].Trim();
        OpenUrl("https://www.google.com/search?q=" + Uri.EscapeDataString(query));

        var old = _connection.Text;
        var oldBrush = _connection.Foreground;

        _connection.Text = "Searching Google";
        _connection.Foreground = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(180, 180, 180));

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _connection.Text = old;
            _connection.Foreground = oldBrush;
        };

        timer.Start();
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void ApplyTheme(string name)
    {
        System.Windows.Media.Color background, panel, foreground, muted, accent, border;
        switch (name)
        {
            case "Light":
                background = System.Windows.Media.Color.FromRgb(247, 247, 247); panel = System.Windows.Media.Color.FromRgb(234, 234, 234);
                foreground = System.Windows.Media.Color.FromRgb(28, 28, 28); muted = System.Windows.Media.Color.FromRgb(100, 100, 100);
                accent = System.Windows.Media.Color.FromRgb(196, 54, 43); border = System.Windows.Media.Color.FromRgb(205, 205, 205);
                break;
            case "Blue":
                background = System.Windows.Media.Color.FromRgb(22, 29, 39); panel = System.Windows.Media.Color.FromRgb(31, 42, 56);
                foreground = System.Windows.Media.Color.FromRgb(231, 238, 246); muted = System.Windows.Media.Color.FromRgb(151, 169, 190);
                accent = System.Windows.Media.Color.FromRgb(70, 150, 220); border = System.Windows.Media.Color.FromRgb(48, 65, 84);
                break;
            default:
                background = System.Windows.Media.Color.FromRgb(31, 31, 31); panel = System.Windows.Media.Color.FromRgb(40, 40, 40);
                foreground = System.Windows.Media.Color.FromRgb(230, 230, 230); muted = System.Windows.Media.Color.FromRgb(153, 153, 153);
                accent = System.Windows.Media.Color.FromRgb(230, 72, 58); border = System.Windows.Media.Color.FromRgb(58, 58, 58);
                break;
        }

        _shell.Background = new SolidColorBrush(background);
        Background = new SolidColorBrush(background);
        _shell.BorderBrush = new SolidColorBrush(border);
        Foreground = new SolidColorBrush(foreground);
        _connection.Foreground = new SolidColorBrush(
             _connected
        ? System.Windows.Media.Color.FromRgb(127, 191, 127)
        : muted);
        _tags.Foreground = new SolidColorBrush(muted);
        _time.Foreground = new SolidColorBrush(muted);

        _lastExpander.Foreground = new SolidColorBrush(foreground);
        _nextExpander.Foreground = new SolidColorBrush(foreground);

        _progress.Foreground = new SolidColorBrush(accent);
        _progress.Background = new SolidColorBrush(panel);
    }

    private void ApplyBadge(bool bot)
    {
        _statusBadge.Background = new SolidColorBrush(bot ? System.Windows.Media.Color.FromRgb(94, 61, 61) : System.Windows.Media.Color.FromRgb(47, 92, 68));
        _statusText.Foreground = System.Windows.Media.Brushes.White;
    }

    private void SavePosition()
    {
        if (WindowState != WindowState.Normal) return;
        _settings.Left = Left;
        _settings.Top = Top;
        SaveSettings();
    }

    private void SaveSettings() => SettingsStore.Save(_settings);

    private static void SetStartWithWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)
            ?? throw new InvalidOperationException("The Windows startup registry key could not be opened.");

        const string valueName = "RadioCompanion";
        if (!enabled)
        {
            key.DeleteValue(valueName, false);
            return;
        }

        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("The application path could not be determined.");
        var entry = System.AppContext.BaseDirectory;
        var command = Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(entry)
            ? $"\"{processPath}\" \"{entry}\""
            : $"\"{processPath}\"";
        key.SetValue(valueName, command);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var source = (HwndSource)PresentationSource.FromVisual(this);
        source.AddHook(WndProc);
        RegisterHotKey(source.Handle, HotkeyPlayPause, 0, VkMediaPlayPause);
        RegisterHotKey(source.Handle, HotkeyStop, 0, VkMediaStop);
        Closed += (_, _) =>
        {
            UnregisterHotKey(source.Handle, HotkeyPlayPause);
            UnregisterHotKey(source.Handle, HotkeyStop);
        };
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            var id = wParam.ToInt32();
            if (id == HotkeyPlayPause) ToggleAudio();
            else if (id == HotkeyStop) StopAudio();
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
