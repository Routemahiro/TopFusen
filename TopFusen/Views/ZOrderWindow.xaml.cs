using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Serilog;
using TopFusen.Services;

namespace TopFusen.Views;

/// <summary>
/// Z順管理ウィンドウ（Phase 9: FR-ZORDER）
/// GongSolutions.Wpf.DragDrop による D&D 並び替え + 即時反映
/// </summary>
public partial class ZOrderWindow : Window
{
    private readonly NoteManager _noteManager;
    private readonly Guid _desktopId;
    private readonly ObservableCollection<ZOrderItem> _items = new();

    /// <summary>コレクション変更処理中フラグ（二重発火防止）</summary>
    private bool _isProcessingChange;

    public ZOrderWindow(NoteManager noteManager)
    {
        _noteManager = noteManager;
        InitializeComponent();

        // 現在のデスクトップ ID を取得
        _desktopId = noteManager.GetCurrentDesktopId() ?? Guid.Empty;

        // ヘッダーにデスクトップ名を表示
        var desktopName = _desktopId != Guid.Empty
            ? noteManager.GetDesktopName(_desktopId)
            : "デスクトップ";
        HeaderText.Text = $"Z順管理 — {desktopName}";

        // リストを構築
        PopulateList();

        // D&D 後の並び替え検知（ObservableCollection.CollectionChanged）
        _items.CollectionChanged += OnItemsCollectionChanged;

        // ListBox にバインド
        ZOrderListBox.ItemsSource = _items;
    }

    /// <summary>
    /// NoteManager から現在デスクトップの付箋を Z順で取得し、リストに反映する
    /// </summary>
    private void PopulateList()
    {
        _isProcessingChange = true;
        try
        {
            _items.Clear();
            var orderedNotes = _noteManager.GetOrderedNotesForDesktop(_desktopId);

            foreach (var (noteId, preview, bgHex) in orderedNotes)
            {
                Color bgColor;
                try
                {
                    bgColor = (Color)ColorConverter.ConvertFromString(bgHex);
                }
                catch
                {
                    bgColor = Color.FromRgb(0xFB, 0xE3, 0x8C); // fallback yellow
                }

                _items.Add(new ZOrderItem
                {
                    NoteId = noteId,
                    DisplayText = preview,
                    BgColor = bgColor,
                });
            }

            NoteCountText.Text = $"{_items.Count} 枚";
        }
        finally
        {
            _isProcessingChange = false;
        }
    }

    /// <summary>
    /// D&D による並び替え後、新しい順序を NoteManager に通知する
    /// GongSolutions.Wpf.DragDrop は Remove + Insert の2回 CollectionChanged を発火するため、
    /// DispatcherPriority.Background で操作完了後に1回だけ処理する
    /// </summary>
    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isProcessingChange) return;

        // D&D 操作完了後に非同期で1回だけ処理
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(SyncZOrderToManager));
    }

    /// <summary>
    /// 現在のリスト順序を NoteManager に反映する
    /// </summary>
    private void SyncZOrderToManager()
    {
        if (_desktopId == Guid.Empty) return;

        var orderedIds = _items.Select(item => item.NoteId).ToList();
        _noteManager.UpdateZOrder(_desktopId, orderedIds);
        Log.Information("ZOrderWindow: D&D による Z順更新 ({Count}枚)", orderedIds.Count);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

/// <summary>
/// Z順リストの1項目（ListBox の DataTemplate 用）
/// </summary>
public class ZOrderItem : INotifyPropertyChanged
{
    public Guid NoteId { get; set; }

    private string _displayText = "（空）";
    public string DisplayText
    {
        get => _displayText;
        set { _displayText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayText))); }
    }

    private Color _bgColor = Color.FromRgb(0xFB, 0xE3, 0x8C);
    public Color BgColor
    {
        get => _bgColor;
        set { _bgColor = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BgColor))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
