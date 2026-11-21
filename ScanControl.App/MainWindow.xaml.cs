using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ScanControl.Core;
using ScanControl.Infrastructure;
using ScanControl.Protocol.BC780XLT;
using Channel = ScanControl.Core.Channel;
using Validation = ScanControl.Core.Validation;

namespace ScanControl.App;

public partial class MainWindow : Window
{
    private Settings settings = new();
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScanControl", "settings.json");
    private readonly ObservableCollection<Channel> channels = [];
    private IScanner? scanner;
    private TransferService? transfer;
    private readonly DispatcherTimer timer = new();
    private readonly AudioInputWorker audio = new();
    private CancellationTokenSource? operation;
    private bool busy, dirty, polling, originSimulation = true, closing;
    private Channel? clipboard;
    private int[]? failedRead;
    private string? projectPath;
    private readonly object logLock = new();
    private readonly ProjectHistory history = new();
    private Project? beforeEdit;

    public MainWindow()
    {
        InitializeComponent();
        BaudBox.ItemsSource = new[] { 2400, 4800, 9600, 19200 };
        BanksList.Items.Add("Alle Bänke"); for (int i = 1; i <= 10; i++) BanksList.Items.Add($"Bank {i}  ·  {(i - 1) * 50 + 1}–{i * 50}");
        BanksList.SelectedIndex = 0; ChannelsGrid.ItemsSource = channels;
        ContentRendered += InitializeAfterRender;
    }
    private bool startupInitialized, audioLoaded, audioLoading;
    private async void InitializeAfterRender(object? sender, EventArgs e)
    {
        if (startupInitialized) return;
        startupInitialized = true;
        StartupDiagnostics.Write("Content rendered; initialization begins");
        StatusLabel.Text = "Programm wird vorbereitet …";
        await Dispatcher.Yield(DispatcherPriority.Background);
        try
        {
            if (!App.SafeStart && !App.SmokeTest && File.Exists(settingsPath))
            {
                StartupDiagnostics.Write("Loading settings");
                try { settings = JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(settingsPath)) ?? new(); }
                catch (Exception error) { StartupDiagnostics.Write("Settings invalid; using defaults without overwriting original: " + error); settings = new(); }
            }
            ApplySettings();
            StartupDiagnostics.Write("Settings applied; audio initialization deferred until settings page");
            timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(settings.PollMs, 500, 60000));
            timer.Tick += async (_, _) => await PollAsync();
            if (settings.AutoConnect && !App.SafeStart && !App.SmokeTest)
            {
                SimulationBox.IsChecked = false; await ConnectAsync();
            }
            else
            {
                await ConnectAsync(); await ReadAsync(Enumerable.Range(121, 10).ToArray(), false);
            }
            await PollAsync();
            StartupDiagnostics.Write("READY: initialization complete; display updated");
            if (App.SmokeTest)
            {
                if (scanner is not SimulatedScanner || channels.Count != 10 || audio.IsStarted || ActualWidth <= 0 || ActualHeight <= 0 || !DisplayFrequency.Text.Contains("149.1250"))
                    throw new InvalidOperationException("Starttest: Oberfläche, Simulator oder verzögerte Audioinitialisierung nicht bestätigt.");
                // Ensure at least one complete render/dispatcher pass before the executable test succeeds.
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Application.Current.Shutdown(0); return;
            }
            timer.Start();
            _ = RefreshPortsAsync();
        }
        catch (Exception error)
        {
            StartupDiagnostics.Write("Initialization failed: " + error);
            if (App.SmokeTest) { Application.Current.Shutdown(1); return; }
            StatusLabel.Text = "Start nicht vollständig: " + error.Message;
            MessageBox.Show(this, "Die Oberfläche ist geöffnet, aber die Vorbereitung ist fehlgeschlagen.\n\n" + error.Message + "\n\nStartprotokoll: " + StartupDiagnostics.LogPath, "ScanControl – Startdiagnose");
        }
    }
    private async Task RefreshPortsAsync()
    {
        try
        {
            var ports = await Task.Run(SerialPort.GetPortNames).WaitAsync(TimeSpan.FromSeconds(3));
            if (!closing) { var text = PortBox.Text; PortBox.ItemsSource = ports.Order().ToArray(); PortBox.Text = text; }
            StartupDiagnostics.Write("COM port list loaded");
        }
        catch (Exception error) { StartupDiagnostics.Write("COM discovery unavailable: " + error.Message); }
    }
    private async Task LoadAudioDevicesAsync()
    {
        if (audioLoaded || audioLoading || closing) return;
        audioLoading = true; AudioLabel.Text = "Aufnahmegeräte werden abgefragt …";
        try
        {
            StartupDiagnostics.Write("Audio discovery requested by settings page");
            var devices = await audio.DevicesAsync();
            if (closing) return;
            AudioBox.ItemsSource = devices; audioLoaded = true;
            AudioBox.SelectedItem = devices.FirstOrDefault(d => d.Id == settings.AudioDeviceId);
            AudioLabel.Text = devices.Count == 0 ? "Kein aktives Windows-Aufnahmegerät gefunden." : "Aufnahmegerät auswählen; Pegel wird nur auf Anforderung verändert.";
            StartupDiagnostics.Write($"Audio discovery complete: {devices.Count} devices");
        }
        catch (Exception error) { AudioLabel.Text = error.Message; StartupDiagnostics.Write("Audio discovery failed: " + error); }
        finally { audioLoading = false; }
    }
    private async void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, Tabs) && IsLoaded && Tabs.SelectedIndex == 2) await LoadAudioDevicesAsync();
    }
    private void ApplySettings()
    {
        PortBox.Text = settings.Port; BaudBox.SelectedItem = settings.Baud; TimeoutBox.Text = settings.TimeoutMs.ToString(); PollBox.Text = settings.PollMs.ToString();
        AutoConnectBox.IsChecked = settings.AutoConnect; AutoBackupBox.IsChecked = settings.AutoBackup; LoggingBox.IsChecked = settings.Logging;
        DirectoryBox.Text = settings.Directory; MaximizedBox.IsChecked = settings.Maximized; if (settings.Maximized) WindowState = WindowState.Maximized;
        LevelSlider.Value = settings.InputLevel; MuteBox.IsChecked = settings.Mute; ThemeBox.SelectedIndex = settings.Theme == "Graphit" ? 1 : 0;
        ApplyTheme();
    }
    private void ApplyTheme() => Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.Theme == "Graphit" ? "#292D32" : "#101214"));
    private void MarkDirty(bool value = true) { dirty = value; DirtyLabel.Text = value ? "Lokal geändert" : "Gespeichert / unverändert"; UpdateHistoryButtons(); }
    private void UpdateHistoryButtons()
    {
        UndoButton.IsEnabled = !busy && history.CanUndo;
        RedoButton.IsEnabled = !busy && history.CanRedo;
    }
    private bool Confirm(string text) => MessageBox.Show(this, text, "ScanControl", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    private bool AllowReplace() => !dirty || Confirm("Lokale Änderungen sind noch nicht gespeichert. Sollen sie überschrieben werden? Vorher wird eine lokale Sicherung angelegt.");
    private void Log(string message)
    {
        if (!settings.Logging) return;
        try { lock (logLock) { var dir = Path.Combine(settings.Directory, "Diagnose"); Directory.CreateDirectory(dir); File.AppendAllText(Path.Combine(dir, $"{DateTime.Today:yyyyMMdd}.log"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); } }
        catch { /* Logging failure must not falsify a transfer outcome. */ }
    }
    private void Error(string message) { StatusLabel.Text = message; Log(message); MessageBox.Show(this, message, "ScanControl – Fehler", MessageBoxButton.OK, MessageBoxImage.Warning); }
    private void Commit()
    {
        if (!ChannelsGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !ChannelsGrid.CommitEdit(DataGridEditingUnit.Row, true)) throw new InvalidDataException("Bitte den ungültigen Tabellenwert korrigieren.");
        Validation.Project(Snapshot());
    }
    private Project Snapshot() => new() { Channels = channels.Select(c => c with { }).ToList(), Simulation = originSimulation };
    private async Task LocalBackupAsync(string reason, bool force = false)
    {
        if (channels.Count > 0 && (settings.AutoBackup || force)) await ProjectFiles.BackupAsync(settings.Directory, Snapshot(), reason);
    }
    private void SetBusy(bool value)
    {
        busy = value; ConnectionControls.IsEnabled = !value; EditControls.IsEnabled = !value; ChannelsGrid.IsReadOnly = value;
        foreach (var tab in Tabs.Items.Cast<TabItem>().Skip(1)) tab.IsEnabled = !value;
        ReadButton.IsEnabled = UploadButton.IsEnabled = !value; CancelButton.IsEnabled = value; RetryButton.IsEnabled = !value && failedRead != null;
        Progress.IsIndeterminate = value; KeyPanel.IsEnabled = !value; BanksList.IsEnabled = !value;
        UpdateHistoryButtons();
    }
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (busy) return;
        var service = transfer;
        operation = new(); SetBusy(true);
        var acquired = false;
        try
        {
            if (service != null) { await service.Gate.WaitAsync(operation.Token); acquired = true; }
            await action(operation.Token);
        }
        catch (OperationCanceledException) { StatusLabel.Text = "Abgebrochen · Vorgang unvollständig. Bei Upload Scannerstand prüfen."; }
        catch (Exception e) { Error("Vorgang fehlgeschlagen / unvollständig: " + e.Message); }
        finally { operation.Dispose(); operation = null; SetBusy(false); if (acquired) service!.Gate.Release(); }
    }
    private async Task ConnectAsync()
    {
        await RunAsync(async ct =>
        {
            scanner?.Dispose(); scanner = null; transfer = null; ConnectionLabel.Text = "Verbindung wird geprüft …";
            try
            {
                settings.Port = PortBox.Text; settings.Baud = (int)(BaudBox.SelectedItem ?? 9600);
                var simulation = SimulationBox.IsChecked == true;
                scanner = simulation ? new SimulatedScanner() : await Task.Run<IScanner>(() => new Bc780Scanner(new SerialTransport(settings, Log)), ct);
                var identity = await scanner.IdentifyAsync(ct); transfer = new(scanner);
                ConnectionLabel.Text = simulation ? "● SIMULATION · kein Hardwaretest" : $"● BC780XLT · {settings.Port}, {settings.Baud} Baud";
                ModeBadge.Text = simulation ? "SIMULATION" : "HARDWARE"; StatusLabel.Text = identity;
            }
            catch { scanner?.Dispose(); scanner = null; ConnectionLabel.Text = "Getrennt · Verbindung fehlgeschlagen"; throw; }
        });
    }
    private async Task PollAsync()
    {
        if (busy || polling || scanner == null || transfer == null || closing) return;
        var service = transfer; if (!await service.Gate.WaitAsync(0)) return;
        polling = true;
        try
        {
            var d = await scanner.DisplayAsync(); DisplayChannel.Text = "CH " + d.Channel; DisplayFrequency.Text = d.Frequency + " MHz";
            DisplayName.Text = d.Line1; DisplayLine2.Text = d.Line2 + "  " + d.Mode;
        }
        catch (Exception e)
        {
            scanner.Dispose(); scanner = null; transfer = null; ConnectionLabel.Text = "● Verbindung unterbrochen"; ModeBadge.Text = "GETRENNT";
            DisplayName.Text = "VERBINDUNG VERLOREN"; StatusLabel.Text = "Neu verbinden erforderlich: " + e.Message; Log(StatusLabel.Text);
        }
        finally { polling = false; service.Gate.Release(); }
    }
    private int[] Scope() => ScopeBox.SelectedIndex switch
    {
        1 => Enumerable.Range(1, 500).Where(n => BanksList.SelectedItems.Cast<string>().Any(s => s.StartsWith($"Bank {(n - 1) / 50 + 1}  ·"))).ToArray(),
        2 => ChannelsGrid.SelectedItems.Cast<Channel>().Select(c => c.Number).Order().ToArray(),
        _ => Enumerable.Range(1, 500).ToArray()
    };
    private void NeedScanner() { if (scanner == null || transfer == null) throw new InvalidOperationException("Bitte zuerst verbinden."); }
    private async Task ReadAsync(int[] numbers, bool ask = true)
    {
        await RunAsync(async ct =>
        {
            NeedScanner(); Commit(); if (numbers.Length == 0) throw new InvalidOperationException("Keine Kanäle/Bänke ausgewählt.");
            await scanner!.IdentifyAsync(ct);
            if (ask && !AllowReplace()) return;
            await LocalBackupAsync("vor-Lesen", dirty); failedRead = numbers;
            var incoming = await transfer!.ReadAsync(numbers, new Progress<string>(s => StatusLabel.Text = s), ct);
            var replaceAll = originSimulation != scanner!.Simulation;
            if (replaceAll && channels.Count > 0 && !Confirm("Der Betriebsmodus hat gewechselt. Den gesamten lokalen Kanalbestand durch die gelesenen Daten ersetzen?")) return;
            history.Record(Snapshot());
            if (replaceAll) channels.Clear();
            foreach (var c in incoming) { var old = channels.FirstOrDefault(x => x.Number == c.Number); if (old != null) channels.Remove(old); channels.Add(c); }
            originSimulation = scanner.Simulation; failedRead = null; RefreshFilter(); MarkDirty();
            StatusLabel.Text = $"{incoming.Count} Kanäle vollständig gelesen · {(scanner.Simulation ? "SIMULATION – kein Hardwaretest" : "Hardware")}";
        });
    }
    private async void Read_Click(object sender, RoutedEventArgs e) => await ReadAsync(sender == RetryButton && failedRead != null ? failedRead : Scope());
    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async ct =>
        {
            NeedScanner(); Commit(); await scanner!.IdentifyAsync(ct);
            if (originSimulation && !scanner!.Simulation) throw new InvalidOperationException("Simulationsdaten sind für Hardware-Uploads gesperrt. Zuerst vom echten Scanner lesen oder ein Hardwareprojekt öffnen.");
            var scope = Scope(); var selected = channels.Where(c => scope.Contains(c.Number)).Select(c => c with { }).OrderBy(c => c.Number).ToArray();
            if (selected.Length != scope.Length || selected.Length == 0) throw new InvalidOperationException("Für den gewählten Umfang fehlen Kanäle. Zuerst vollständig lesen oder einen kleineren Umfang wählen.");
            if (!scanner!.Simulation && selected.Any(c => c.Trunk)) throw new NotSupportedException("Trunking-Upload wird nicht unterstützt.");
            var old = await transfer!.ReadAsync(scope, new Progress<string>(s => StatusLabel.Text = "Sicherung · " + s), ct);
            var backup = await ProjectFiles.BackupAsync(settings.Directory, new Project { Channels = old, Simulation = scanner.Simulation }, "vor-Upload");
            var changes = selected.Where(c => c != old.First(x => x.Number == c.Number)).ToArray();
            if (!scanner.Simulation && changes.Any(c => old.First(x => x.Number == c.Number).Trunk))
                throw new NotSupportedException("Der Scanner enthält in den zu ändernden Speicherplätzen Trunkingkanäle. Diese können hier nicht sicher überschrieben werden.");
            if (changes.Length == 0) { StatusLabel.Text = "Keine Änderungen am Scanner erforderlich."; return; }
            var detail = string.Join(Environment.NewLine, changes.Select(c => { var o = old.First(x => x.Number == c.Number); return $"Kanal {c.Number:000}:\n  ALT {JsonSerializer.Serialize(o)}\n  NEU {JsonSerializer.Serialize(c)}"; }));
            if (!Review($"{(scanner.Simulation ? "SIMULATION – kein Hardwaretest" : "HARDWARE-UPLOAD")}\n{changes.Length} geänderte Kanäle. Sicherung: {backup}\n\n{detail}")) { StatusLabel.Text = "Upload vor dem Schreiben abgebrochen."; return; }
            ct.ThrowIfCancellationRequested();
            var journal = backup + ".transfer.json";
            await ProjectFiles.AtomicWriteAsync(journal, JsonSerializer.Serialize(new { State = "Unvollständig", Backup = backup, Channels = changes.Select(c => c.Number), StartedAt = DateTimeOffset.UtcNow }));
            // Check the backup still describes the device immediately before any write.
            var beforeWrite = await transfer.ReadAsync(changes.Select(c => c.Number), new Progress<string>(s => StatusLabel.Text = "Vorprüfung · " + s), ct);
            if (beforeWrite.Any(c => c != old.First(x => x.Number == c.Number))) throw new IOException("Scanner wurde seit der Sicherung verändert. Upload neu starten.");
            await transfer.WriteAndVerifyAsync(changes, new Progress<string>(s => StatusLabel.Text = s), ct);
            await ProjectFiles.AtomicWriteAsync(journal, JsonSerializer.Serialize(new { State = "Verifiziert", Backup = backup, Channels = changes.Select(c => c.Number), CompletedAt = DateTimeOffset.UtcNow }));
            StatusLabel.Text = scanner.Simulation ? "SIMULATION: Upload und Vergleich abgeschlossen – kein Hardwaretest." : "Upload vollständig abgeschlossen · alle geschriebenen Kanäle verifiziert.";
        });
    }
    private bool Review(string text, string title = "Änderungen prüfen und Upload bestätigen", string accept = "Jetzt übertragen")
    {
        var dialog = new Window { Title = title, Owner = this, Width = 980, Height = 650, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background, Foreground = Brushes.White };
        var dock = new DockPanel { Margin = new Thickness(16) }; var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Abbrechen", IsCancel = true }; var ok = new Button { Content = accept, Background = new SolidColorBrush(Color.FromRgb(165, 20, 28)) };
        cancel.Click += (_, _) => dialog.DialogResult = false; ok.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(ok); DockPanel.SetDock(buttons, Dock.Bottom); dock.Children.Add(buttons);
        dock.Children.Add(new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); dialog.Content = dock; return dialog.ShowDialog() == true;
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async ct =>
        {
            Commit(); var dialog = new SaveFileDialog { Filter = "ScanControl Projekt|*.json", FileName = projectPath ?? "ScanControl.json" }; if (dialog.ShowDialog(this) != true) return;
            if (File.Exists(dialog.FileName)) { var previous = await ProjectFiles.LoadAsync(dialog.FileName); await ProjectFiles.BackupAsync(settings.Directory, previous, "vor-Dateiueberschreiben"); }
            await ProjectFiles.SaveAsync(dialog.FileName, Snapshot()); projectPath = dialog.FileName; MarkDirty(false); StatusLabel.Text = "Projekt gespeichert: " + projectPath;
        });
    }
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async ct =>
        {
            Commit(); var dialog = new OpenFileDialog { Filter = "ScanControl Projekt / Sicherung|*.json" }; if (dialog.ShowDialog(this) != true) return;
            var loaded = await ProjectFiles.LoadAsync(dialog.FileName); if (!AllowReplace()) return;
            await LocalBackupAsync("vor-Oeffnen", dirty); channels.Clear(); foreach (var c in loaded.Channels) channels.Add(c);
            history.Clear(); originSimulation = loaded.Simulation; projectPath = dialog.FileName; MarkDirty(false); RefreshFilter();
            StatusLabel.Text = loaded.Simulation ? "Simulationsprojekt geladen – Hardware-Upload gesperrt." : "Projekt zur Prüfung geladen; Scanner unverändert.";
        });
    }
    private async void Backup_Click(object sender, RoutedEventArgs e) => await RunAsync(async ct => { NeedScanner(); var read = await transfer!.ReadAsync(Enumerable.Range(1, 500), new Progress<string>(s => StatusLabel.Text = s), ct); var path = await ProjectFiles.BackupAsync(settings.Directory, new Project { Channels = read, Simulation = scanner!.Simulation }, "Scannerkanaele"); StatusLabel.Text = "Alle 500 Kanäle gesichert: " + path; });
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async ct =>
        {
            Commit(); var dialog = new OpenFileDialog { Filter = "ScanControl CSV|*.csv" }; if (dialog.ShowDialog(this) != true) return;
            var list = ProjectFiles.ImportCsv(await File.ReadAllTextAsync(dialog.FileName, ct));
            var preview = $"CSV-VORSCHAU · {list.Count} validierte Kanäle\nDie Übernahme ersetzt den gesamten lokalen Kanalbestand. Der Scanner bleibt unverändert.\n\n" +
                string.Join(Environment.NewLine, list.Select(c => $"{c.Number:000} | Bank {c.Bank} | {c.FrequencyMHz:F4} MHz | {c.Name} | {c.Modulation} | L/O: {c.Lockout}"));
            if (!Review(preview, "CSV-Import prüfen", "In Tabelle übernehmen") || !AllowReplace()) return;
            await LocalBackupAsync("vor-CSV", true); history.Record(Snapshot()); channels.Clear(); foreach (var c in list) channels.Add(c); MarkDirty(); RefreshFilter(); StatusLabel.Text = "CSV validiert und importiert; Herkunftsmodus des Projekts beibehalten.";
        });
    }
    private async void Export_Click(object sender, RoutedEventArgs e) => await RunAsync(async ct => { Commit(); var dialog = new SaveFileDialog { Filter = "ScanControl CSV|*.csv", FileName = "ScanControl.csv" }; if (dialog.ShowDialog(this) == true) { await ProjectFiles.AtomicWriteAsync(dialog.FileName, ProjectFiles.ExportCsv(channels)); StatusLabel.Text = "CSV exportiert."; } });
    private async void Key_Click(object sender, RoutedEventArgs e) => await RunAsync(async ct => { NeedScanner(); await scanner!.KeyAsync((string)((Button)sender).Tag, ct); StatusLabel.Text = "Taste gesendet; tatsächlichen Zustand in der Anzeige prüfen."; });
    private async void Connect_Click(object sender, RoutedEventArgs e) => await ConnectAsync();
    private void Disconnect_Click(object sender, RoutedEventArgs e) { if (busy || polling) return; scanner?.Dispose(); scanner = null; transfer = null; ConnectionLabel.Text = "Getrennt"; ModeBadge.Text = "GETRENNT"; DisplayName.Text = "NICHT VERBUNDEN"; }
    private void Cancel_Click(object sender, RoutedEventArgs e) => operation?.Cancel();
    private void Navigate_Click(object sender, RoutedEventArgs e) => Tabs.SelectedIndex = int.Parse((string)((Button)sender).Tag);
    private void About_Click(object sender, RoutedEventArgs e)
    {
        var version = typeof(MainWindow).Assembly.GetName().Version;
        var dialog = new Window
        {
            Title = "Über ScanControl", Owner = this, Icon = Icon, Width = 520, Height = 410,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Background, Foreground = Brushes.White
        };
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/ScanControl.png")), Width = 88, Height = 88, HorizontalAlignment = HorizontalAlignment.Left });
        panel.Children.Add(new TextBlock { Text = "ScanControl", FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(new TextBlock { Text = $"Version {version?.ToString(3)} · Uniden BC780XLT", Foreground = Brushes.Silver, Margin = new Thickness(0, 4, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "Scannersteuerung und Kanalverwaltung\n\nEntwicklungsstand: Hardwareprüfung ausstehend.\nSimulationsergebnisse sind keine Hardwaretests.", TextWrapping = TextWrapping.Wrap });
        var close = new Button { Content = "Schließen", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        close.Click += (_, _) => dialog.Close(); panel.Children.Add(close); dialog.Content = panel; dialog.ShowDialog();
    }
    private void RefreshFilter()
    {
        if (ChannelsGrid == null || BanksList == null) return;
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(channels);
        var query = SearchBox?.Text.Trim() ?? "";
        view.Filter = o =>
        {
            var c = (Channel)o;
            var inBank = BanksList.SelectedIndex <= 0 || BanksList.SelectedItems.Cast<string>().Any(s => s.StartsWith($"Bank {c.Bank}  ·"));
            var matches = query.Length == 0 || $"{c.Number} {c.Name} {c.Modulation} {c.FrequencyMHz.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}".Contains(query.Replace(',', '.'), StringComparison.OrdinalIgnoreCase);
            return inBank && matches;
        };
        view.SortDescriptions.Clear(); view.SortDescriptions.Add(new(nameof(Channel.Number), ListSortDirection.Ascending));
        if (ChannelCountLabel != null) ChannelCountLabel.Text = $"{view.Cast<Channel>().Count()} sichtbar · {channels.Count} Kanäle im Projekt";
    }
    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || busy) return;
        try { Commit(); RefreshFilter(); } catch (Exception ex) { StatusLabel.Text = ex.Message; }
    }
    private void Grid_BeginEdit(object sender, DataGridBeginningEditEventArgs e) { if (!busy) beforeEdit = Snapshot(); }
    private void Banks_Changed(object sender, SelectionChangedEventArgs e) { if (!busy) RefreshFilter(); }
    private void Grid_Edit(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit)
        {
            if (e.EditingElement is TextBox text) text.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (e.EditingElement is CheckBox check) check.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateSource();
            if (beforeEdit != null && !beforeEdit.Channels.SequenceEqual(channels)) history.Record(beforeEdit);
            MarkDirty();
        }
        beforeEdit = null;
    }
    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return; int n = Enumerable.Range(1, 500).FirstOrDefault(n => channels.All(c => c.Number != n));
        if (n == 0) { Error("Alle 500 Speicherplätze sind vorhanden. Vorhandenen Kanal bearbeiten."); return; }
        history.Record(Snapshot()); var c = new Channel { Number = n }; channels.Add(c); MarkDirty(); SearchBox.Clear(); BanksList.SelectedIndex = 0; RefreshFilter(); ChannelsGrid.SelectedItem = c; ChannelsGrid.ScrollIntoView(c);
    }
    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        var selected = ChannelsGrid.SelectedItems.Cast<Channel>().ToArray();
        await RunAsync(async ct => { Commit(); if (selected.Length == 0 || !Confirm($"{selected.Length} Kanäle lokal leeren?")) return; await LocalBackupAsync("vor-Leeren", true); history.Record(Snapshot()); foreach (var c in selected) { channels.Remove(c); channels.Add(new Channel { Number = c.Number }); } MarkDirty(); RefreshFilter(); });
    }
    private void Copy_Click(object sender, RoutedEventArgs e) { if (ChannelsGrid.SelectedItem is Channel c) clipboard = c with { }; }
    private async void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (clipboard == null || ChannelsGrid.SelectedItem is not Channel target) return;
        await RunAsync(async ct => { Commit(); await LocalBackupAsync("vor-Einfuegen", true); history.Record(Snapshot()); channels.Remove(target); channels.Add(clipboard with { Number = target.Number }); MarkDirty(); RefreshFilter(); });
    }
    private async void Undo_Click(object sender, RoutedEventArgs e) => await RestoreHistoryAsync(false);
    private async void Redo_Click(object sender, RoutedEventArgs e) => await RestoreHistoryAsync(true);
    private async Task RestoreHistoryAsync(bool redo)
    {
        await RunAsync(async ct =>
        {
            Commit();
            if (redo ? !history.CanRedo : !history.CanUndo) return;
            await LocalBackupAsync("vor-Rueckgaengig");
            var p = redo ? history.Redo(Snapshot()) : history.Undo(Snapshot());
            channels.Clear(); foreach (var c in p.Channels) channels.Add(c);
            originSimulation = p.Simulation; MarkDirty(); RefreshFilter();
            StatusLabel.Text = redo ? "Lokale Änderung wiederholt · Scanner unverändert." : "Lokale Änderung rückgängig gemacht · Scanner unverändert.";
        });
    }
    private async void Audio_Changed(object sender, SelectionChangedEventArgs e)
    {
        try { if (AudioBox.SelectedItem is InputDevice d) { var current = await audio.SelectAsync(d.Id); if (!closing && AudioBox.SelectedItem is InputDevice selected && selected.Id == d.Id) AudioLabel.Text = $"Windows aktuell: {current.Level}% · Mute {current.Mute}"; } }
        catch (Exception ex) { AudioLabel.Text = ex.Message; }
    }
    private async void AudioApply_Click(object sender, RoutedEventArgs e) { try { var v = await audio.SetAsync((int)LevelSlider.Value, MuteBox.IsChecked == true); if (!closing) AudioLabel.Text = $"Windows aktuell: {v.Level}% · Mute {v.Mute}"; } catch (Exception ex) { if (!closing) Error(ex.Message); } }
    private async void AudioRefresh_Click(object sender, RoutedEventArgs e) { try { var v = await audio.ReadAsync(); if (!closing) AudioLabel.Text = $"Windows aktuell: {v.Level}% · Mute {v.Mute}"; } catch (Exception ex) { if (!closing) Error(ex.Message); } }
    private async void SettingsSave_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async ct =>
        {
            if (!int.TryParse(TimeoutBox.Text, out var timeout) || timeout < 250 || timeout > 30000 || !int.TryParse(PollBox.Text, out var poll) || poll < 500 || poll > 60000) throw new InvalidDataException("Timeout: 250–30000 ms; Anzeigeintervall: 500–60000 ms.");
            var dir = Path.GetFullPath(DirectoryBox.Text); Directory.CreateDirectory(dir);
            settings.Port = PortBox.Text; settings.Baud = (int)(BaudBox.SelectedItem ?? 9600); settings.TimeoutMs = timeout; settings.PollMs = poll;
            settings.AutoConnect = AutoConnectBox.IsChecked == true; settings.AutoBackup = AutoBackupBox.IsChecked == true; settings.Logging = LoggingBox.IsChecked == true;
            settings.Directory = dir; settings.Maximized = MaximizedBox.IsChecked == true; settings.Theme = ThemeBox.SelectedIndex == 1 ? "Graphit" : "Dunkel";
            settings.AudioDeviceId = (AudioBox.SelectedItem as InputDevice)?.Id ?? ""; settings.InputLevel = (int)LevelSlider.Value; settings.Mute = MuteBox.IsChecked == true;
            await ProjectFiles.AtomicWriteAsync(settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            timer.Interval = TimeSpan.FromMilliseconds(poll); ApplyTheme(); StatusLabel.Text = "Einstellungen gespeichert.";
        });
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (App.SmokeTest) { closing = true; timer.Stop(); scanner?.Dispose(); audio.Dispose(); return; }
        if (busy || polling) { e.Cancel = true; StatusLabel.Text = "Bitte laufenden Vorgang abbrechen und sein Ende abwarten."; return; }
        if (dirty && !Confirm("Ungespeicherte lokale Änderungen verwerfen und ScanControl schließen?")) { e.Cancel = true; return; }
        closing = true; timer.Stop(); scanner?.Dispose(); audio.Dispose();
    }
}
