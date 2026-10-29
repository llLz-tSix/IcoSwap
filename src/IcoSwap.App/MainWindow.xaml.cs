using System.Collections.ObjectModel;
using IcoSwap.Core;
using IcoSwap.Core.Apply;
using IcoSwap.Core.Matching;
using IcoSwap.Core.Packs;
using IcoSwap.Core.Targets;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace IcoSwap.App;

public sealed partial class MainWindow : Window
{
    private readonly ApplyEngine _engine;
    private readonly ObservableCollection<MatchGroup> _groups = new();
    private readonly ObservableCollection<UnmatchedRow> _unmatched = new();
    private readonly ObservableCollection<AppliedRow> _applied = new();
    private Task<TargetCatalog>? _catalogTask;
    private IconPack? _pack;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();
        // Размер в физических пикселях — учитываем масштаб экрана и центрируем
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale = GetDpiForWindow(hwnd) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int w = Math.Min((int)(1180 * scale), area.Width), h = Math.Min((int)(800 * scale), area.Height);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "IcoSwap.ico");
        if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);

        _engine = new ApplyEngine(new BackupStore())
        {
            ElevatedRunner = job => job.RunElevated(Environment.ProcessPath!),
        };

        GroupsSource.Source = _groups;
        UnmatchedGrid.ItemsSource = _unmatched;
        AppliedList.ItemsSource = _applied;

        // Каталог целей строим заранее, пока пользователь выбирает пак
        _catalogTask = Task.Run(() => TargetCatalog.Discover());
        ReloadApplied();
        UpdateUi();

        // IcoSwap.exe <папка|zip> — сразу открыть пак
        var cmd = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
        if (cmd != null && (Directory.Exists(cmd) || File.Exists(cmd)))
            Root.Loaded += async (_, _) => await LoadPackAsync(cmd);
    }

    // ---------------- загрузка пака ----------------

    private async void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = FileDialogs.PickFolder(Hwnd, "Папка с иконками");
        if (folder != null) await LoadPackAsync(folder);
    }

    private async void PickFile_Click(object sender, RoutedEventArgs e)
    {
        var file = FileDialogs.PickFile(Hwnd, "Пак иконок",
            ("Паки иконок (*.zip, *.icl, *.dll, *.ico, *.png, *.svg)", "*.zip;*.icl;*.dll;*.ico;*.png;*.svg"),
            ("Все файлы", "*.*"));
        if (file != null) await LoadPackAsync(file);
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (!_busy && e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Загрузить пак иконок";
        }
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (_busy || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        var item = items.FirstOrDefault();
        if (item == null) return;
        if (item is StorageFile f && !PackLoader.IsSupportedFile(f.Path))
        {
            ShowInfo("Этот файл не похож на пак иконок", "Перетащите папку, zip-архив, .icl или .dll.", InfoBarSeverity.Warning);
            return;
        }
        await LoadPackAsync(item.Path);
    }

    private async Task LoadPackAsync(string path)
    {
        SetBusy(true, "Загрузка пака…");
        Info.IsOpen = false;
        try
        {
            var progress = new Progress<string>(s => StatusText.Text = s);
            var pack = await Task.Run(() => PackLoader.Import(path, progress));
            StatusText.Text = "Поиск ярлыков и системных значков…";
            var catalog = await (_catalogTask ??= Task.Run(() => TargetCatalog.Discover()));
            StatusText.Text = "Сопоставление…";
            var plan = await Task.Run(() => Matcher.Build(pack, catalog));
            _pack = pack;
            ShowPlan(plan);

            PackTitle.Text = pack.Name;
            PackSubtitle.Text = $"{pack.Icons.Count} иконок · сопоставлено целей: {plan.Items.Count} · нераспознано иконок: {plan.Unmatched.Count}";
            if (pack.Warnings.Count > 0)
                ShowInfo($"Предупреждения при чтении пака: {pack.Warnings.Count}", string.Join("\n", pack.Warnings.Take(6)), InfoBarSeverity.Warning);
            Tabs.SelectedItem = plan.Items.Count == 0 && plan.Unmatched.Count > 0 ? TabUnmatched : TabPreview;
            StatusText.Text = plan.Items.Count > 0 ? "Проверьте список и нажмите «Применить»." : "Автоматически ничего не сопоставилось — назначьте иконки вручную.";
            _ = LoadBeforeImagesAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = "";
            ShowInfo("Не удалось загрузить пак", ex.Message, InfoBarSeverity.Error);
        }
        finally { SetBusy(false); }
    }

    private void ShowPlan(MatchPlan plan)
    {
        _groups.Clear();
        foreach (var g in plan.Items.GroupBy(i => i.Target.Kind).OrderBy(g => g.Key))
        {
            var group = new MatchGroup { Key = $"{IconTarget.KindTitle(g.Key)} ({g.Count()})", Kind = g.Key };
            foreach (var item in g) group.Add(NewRow(item));
            _groups.Add(group);
        }
        _unmatched.Clear();
        foreach (var u in plan.Unmatched) _unmatched.Add(new UnmatchedRow(u));
        UpdateUi();
    }

    private MatchRow NewRow(MatchItem item)
    {
        var row = new MatchRow(item);
        row.EnabledChanged += UpdateUi;
        return row;
    }

    /// <summary>Текущие иконки целей («до») — в фоне, чтобы не тормозить список.</summary>
    private async Task LoadBeforeImagesAsync(IEnumerable<MatchRow>? rows = null)
    {
        foreach (var row in (rows ?? _groups.SelectMany(g => g)).ToList())
        {
            var png = await Task.Run(() => ShellImage.GetCurrentIconPng(row.Item.Target, 64));
            row.BeforeImage = await Images.FromPngBytes(png);
        }
    }

    // ---------------- ручное назначение ----------------

    private async void Unmatched_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not UnmatchedRow row || _catalogTask == null) return;
        var catalog = await _catalogTask;

        var box = new AutoSuggestBox
        {
            PlaceholderText = "Ярлык, системный значок, диск или расширение (.log)",
            QueryIcon = new SymbolIcon(Symbol.Find),
            Width = 460,
        };
        TargetChoice? chosen = null;
        var all = catalog.Targets.OrderBy(t => t.Kind).ThenBy(t => t.DisplayName).ToList();
        List<TargetChoice> Suggest(string text)
        {
            var list = new List<TargetChoice> { new() { Label = "📁  Выбрать папку на диске…", PickFolder = true } };
            text = text.Trim();
            if (text.StartsWith('.') && text.Length > 1)
            {
                var ft = catalog.GetFileType(text);
                list.Add(ft != null
                    ? new TargetChoice { Label = $"Тип файлов {ft.DisplayName}" + (ft.Note != null ? $" ({ft.Note})" : ""), Target = ft }
                    : new TargetChoice { Label = $"Расширение {text} не зарегистрировано или иконку для него сменить нельзя" });
            }
            list.AddRange(all.Where(t => text.Length == 0 || t.DisplayName.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                                         || (t.Location?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false))
                .Take(60)
                .Select(t => new TargetChoice { Label = $"{t.DisplayName} — {IconTarget.KindTitle(t.Kind)}" + (t.Location != null ? $" · {t.Location}" : ""), Target = t }));
            return list;
        }
        box.ItemsSource = Suggest("");
        box.TextChanged += (s, a) => { if (a.Reason == AutoSuggestionBoxTextChangeReason.UserInput) s.ItemsSource = Suggest(s.Text); };
        box.SuggestionChosen += (s, a) => chosen = a.SelectedItem as TargetChoice;
        box.GotFocus += (_, _) => box.IsSuggestionListOpen = true;

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = $"Назначить «{row.Name}»",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new Image { Source = row.Image, Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Left },
                    box,
                },
            },
            PrimaryButtonText = "Назначить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        IconTarget? target = chosen?.Target;
        if (chosen == null && box.Text.Trim().StartsWith('.')) target = catalog.GetFileType(box.Text.Trim());
        if (chosen?.PickFolder == true)
        {
            var folder = FileDialogs.PickFolder(Hwnd, $"Папка для иконки «{row.Name}»");
            if (folder == null) return;
            target = TargetCatalog.CreateFolderTarget(folder);
        }
        if (target == null)
        {
            ShowInfo("Цель не выбрана", "Выберите вариант из списка подсказок.", InfoBarSeverity.Informational);
            return;
        }
        AssignManually(row, target);
    }

    private void AssignManually(UnmatchedRow row, IconTarget target)
    {
        // Если цель уже есть в списке — меняем иконку, иначе добавляем строку
        foreach (var g in _groups)
        {
            var existing = g.FirstOrDefault(r => r.Item.Target.Id.Equals(target.Id, StringComparison.OrdinalIgnoreCase));
            if (existing != null) g.Remove(existing);
        }
        var item = new MatchItem { Target = target, Icon = row.Icon, Score = 1, Reason = "вручную" };
        var group = _groups.FirstOrDefault(g => g.Kind == target.Kind);
        if (group == null)
        {
            group = new MatchGroup { Key = IconTarget.KindTitle(target.Kind), Kind = target.Kind };
            int idx = 0;
            while (idx < _groups.Count && _groups[idx].Kind < target.Kind) idx++;
            _groups.Insert(idx, group);
        }
        var newRow = NewRow(item);
        group.Insert(0, newRow);
        _unmatched.Remove(row);
        UpdateUi();
        _ = LoadBeforeImagesAsync(new[] { newRow });
        ShowInfo("Назначено", $"«{row.Name}» → {target.DisplayName}. Строка добавлена в предпросмотр.", InfoBarSeverity.Success);
    }

    // ---------------- применение и откат ----------------

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var rows = _groups.SelectMany(g => g).Where(r => r.Enabled).ToList();
        if (rows.Count == 0) return;
        var requests = rows.Select(r => new ApplyRequest(r.Item.Target, r.Item.Icon.IcoPath)).ToList();
        SetBusy(true, "Применение…");
        try
        {
            var progress = new Progress<string>(s => StatusText.Text = s);
            var report = await Task.Run(() => _engine.Apply(requests, progress));
            ShowReport("Иконки применены", report);
            ReloadApplied();
            await LoadBeforeImagesAsync(rows);
        }
        catch (Exception ex) { ShowInfo("Ошибка применения", ex.Message, InfoBarSeverity.Error); }
        finally { SetBusy(false); }
    }

    private async void RestoreAll_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.Store.All().Count == 0)
        {
            ShowInfo("Нечего откатывать", "Все иконки и так системные.", InfoBarSeverity.Informational);
            return;
        }
        var confirm = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Откатить всё?",
            Content = $"Будут возвращены исходные иконки для {_engine.Store.All().Count} объектов.",
            PrimaryButtonText = "Откатить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        await RestoreAsync(null);
    }

    private async void RestoreOne_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) await RestoreAsync(new[] { id });
    }

    private async Task RestoreAsync(string[]? ids)
    {
        SetBusy(true, "Откат…");
        try
        {
            var progress = new Progress<string>(s => StatusText.Text = s);
            var report = await Task.Run(() => ids == null ? _engine.RestoreAll(progress) : _engine.Restore(ids, progress));
            ShowReport("Исходные иконки возвращены", report);
            ReloadApplied();
            await LoadBeforeImagesAsync();
        }
        catch (Exception ex) { ShowInfo("Ошибка отката", ex.Message, InfoBarSeverity.Error); }
        finally { SetBusy(false); }
    }

    private async void Refresh_Click(SplitButton sender, SplitButtonClickEventArgs args)
    {
        SetBusy(true, "Обновление Проводника…");
        await Task.Run(() => ShellRefresher.Refresh());
        SetBusy(false, "Проводник обновлён.");
        await LoadBeforeImagesAsync();
    }

    private async void DeepRefresh_Click(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Перезапустить Проводник?",
            Content = "Окна Проводника закроются, панель задач на пару секунд исчезнет. Кэш иконок будет очищен — это помогает, если иконки на панели задач или у папок не обновились.",
            PrimaryButtonText = "Перезапустить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        SetBusy(true, "Перезапуск Проводника…");
        await Task.Run(ShellRefresher.DeepRefresh);
        SetBusy(false, "Кэш иконок очищен, Проводник перезапущен.");
        await LoadBeforeImagesAsync();
    }

    private void ShowReport(string title, ApplyReport r)
    {
        var lines = new List<string> { r.ToString() };
        lines.AddRange(r.Failed.Take(8).Select(f => $"• {f.Target}: {f.Error}"));
        if (r.SkippedAdmin.Count > 0) lines.Add("Пропущено без прав администратора: " + string.Join(", ", r.SkippedAdmin.Take(5)) + (r.SkippedAdmin.Count > 5 ? "…" : ""));
        if (r.Succeeded.Count > 0) lines.Add("Если где-то видна старая иконка — нажмите «Обновить» ▸ «Глубокая очистка кэша».");
        var severity = r.Failed.Count > 0 ? InfoBarSeverity.Warning : r.SkippedAdmin.Count > 0 ? InfoBarSeverity.Informational : InfoBarSeverity.Success;
        ShowInfo(title, string.Join("\n", lines), severity);
        StatusText.Text = r.ToString();
    }

    // ---------------- вспомогательное ----------------

    private void ReloadApplied()
    {
        _engine.Store.Reload();
        _applied.Clear();
        foreach (var r in _engine.Store.All()) _applied.Add(new AppliedRow(r));
        TabApplied.Text = _applied.Count > 0 ? $"Применено ({_applied.Count})" : "Применено";
        UpdateUi();
    }

    private void UpdateUi()
    {
        int total = _groups.Sum(g => g.Count);
        int enabled = _groups.Sum(g => g.Count(r => r.Enabled));
        ApplyText.Text = total > 0 ? $"Применить ({enabled})" : "Применить";
        ApplyButton.IsEnabled = !_busy && enabled > 0;
        RestoreAllButton.IsEnabled = !_busy && _applied.Count > 0;
        PickFolderButton.IsEnabled = PickFileButton.IsEnabled = !_busy;
        EmptyPreview.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyApplied.Visibility = _applied.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TabUnmatched.Text = _unmatched.Count > 0 ? $"Нераспознанные ({_unmatched.Count})" : "Нераспознанные";
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        Busy.IsActive = busy;
        if (status != null) StatusText.Text = status;
        UpdateUi();
    }

    private void ShowInfo(string title, string message, InfoBarSeverity severity)
    {
        Info.Title = title;
        Info.Message = message;
        Info.Severity = severity;
        Info.IsOpen = true;
    }

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var sel = sender.SelectedItem;
        if (PreviewPanel == null) return; // событие во время InitializeComponent
        PreviewPanel.Visibility = sel == TabPreview ? Visibility.Visible : Visibility.Collapsed;
        UnmatchedPanel.Visibility = sel == TabUnmatched ? Visibility.Visible : Visibility.Collapsed;
        AppliedPanel.Visibility = sel == TabApplied ? Visibility.Visible : Visibility.Collapsed;
        SelectAllPanel.Visibility = sel == TabPreview ? Visibility.Visible : Visibility.Collapsed;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SetAll(true);
    private void SelectNone_Click(object sender, RoutedEventArgs e) => SetAll(false);

    private void SetAll(bool value)
    {
        foreach (var r in _groups.SelectMany(g => g)) r.Enabled = value;
        UpdateUi();
    }
}
