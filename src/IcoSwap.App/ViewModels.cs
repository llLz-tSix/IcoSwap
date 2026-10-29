using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using IcoSwap.Core.Apply;
using IcoSwap.Core.Matching;
using IcoSwap.Core.Packs;
using IcoSwap.Core.Targets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace IcoSwap.App;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Строка превью «что на что заменится».</summary>
public sealed class MatchRow : Observable
{
    public MatchItem Item { get; }
    public event Action? EnabledChanged;

    public MatchRow(MatchItem item)
    {
        Item = item;
        AfterImage = Images.FromFile(item.Icon.PreviewPath);
    }

    public bool Enabled
    {
        get => Item.Enabled;
        set
        {
            if (Item.Enabled == value) return;
            Item.Enabled = value;
            Raise(nameof(Enabled));
            EnabledChanged?.Invoke();
        }
    }

    public string Title => Item.Target.DisplayName;
    public string Subtitle => Item.Target.Location ?? IconTarget.KindTitle(Item.Target.Kind);
    public string IconInfo => $"{Item.Icon.Source} · {Item.Reason}" + (Item.Score < 0.999 ? $" {Item.Score:P0}" : "");
    public ImageSource? AfterImage { get; }

    private ImageSource? _before;
    public ImageSource? BeforeImage { get => _before; set => Set(ref _before, value); }

    public Visibility AdminVisibility => Item.Target.RequiresAdmin ? Visibility.Visible : Visibility.Collapsed;
    public string Note => Item.Target.Note ?? "";
    public Visibility NoteVisibility => string.IsNullOrEmpty(Item.Target.Note) ? Visibility.Collapsed : Visibility.Visible;
}

public sealed class MatchGroup : ObservableCollection<MatchRow>
{
    public required string Key { get; init; }
    public required TargetKind Kind { get; init; }
}

/// <summary>Нераспознанная иконка, которую можно назначить вручную.</summary>
public sealed class UnmatchedRow
{
    public PackIcon Icon { get; }
    public UnmatchedRow(PackIcon icon)
    {
        Icon = icon;
        Image = Images.FromFile(icon.PreviewPath);
    }
    public string Name => Icon.Name;
    public string Source => Icon.Source;
    public ImageSource? Image { get; }
}

/// <summary>Применённое изменение (из бэкапа).</summary>
public sealed class AppliedRow : Observable
{
    public BackupRecord Record { get; }
    public AppliedRow(BackupRecord r)
    {
        Record = r;
        AppliedImage = r.AppliedIcon != null && File.Exists(r.AppliedIcon) ? Images.FromIco(r.AppliedIcon) : null;
    }
    public string Title => Record.DisplayName;
    public string Subtitle => $"{IconTarget.KindTitle(Record.Kind)} · {Record.Location ?? ""} · {Record.LastAppliedUtc.ToLocalTime():g}";
    public ImageSource? AppliedImage { get; }
    public Visibility AdminVisibility => Record.RequiresAdmin ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>Вариант в поиске цели для ручного назначения.</summary>
public sealed class TargetChoice
{
    public required string Label { get; init; }
    public IconTarget? Target { get; init; }
    public bool PickFolder { get; init; }
    public override string ToString() => Label;
}

internal static class Images
{
    public static ImageSource? FromFile(string path)
    {
        if (!File.Exists(path)) return null;
        return new BitmapImage(new Uri(path));
    }

    /// <summary>Превью для .ico из пака (рядом лежит PNG в previews\).</summary>
    public static ImageSource? FromIco(string icoPath)
    {
        var png = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(icoPath)!)!, "previews", Path.GetFileNameWithoutExtension(icoPath) + ".png");
        return FromFile(png);
    }

    public static async Task<ImageSource?> FromPngBytes(byte[]? png)
    {
        if (png == null) return null;
        var bmp = new BitmapImage();
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        await bmp.SetSourceAsync(stream);
        return bmp;
    }
}
