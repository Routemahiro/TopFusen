# SESSION_HANDOVER_20260206_PART3 — Phase 2 実装完了

> 作成日: 2026-02-06
> セッション種別: 実装（Phase 2: Win32 Interop + モード切替）
> 前回: SESSION_HANDOVER_20260206_PART2（Phase 1 完了）
> 次回: Phase 3（付箋の移動・リサイズ + 基本UI）から開始

---

## 1. 今回実施した内容

### 1.1 Phase 2: Win32 Interop + モード切替 — 全3タスク + 検証完了

| タスク | 内容 | 結果 |
|--------|------|------|
| P2-1 | Win32 Interop ヘルパー拡張（WS_EX_TRANSPARENT 等の定数追加） | ✅ |
| P2-2 | NoteWindow にクリック透過の ON/OFF 実装（三重制御方式） | ✅ |
| P2-3 | AppHost にモード管理（NoteManager.SetEditMode + トレイ連動） | ✅ |

### 1.2 P2-VERIFY 検証結果（★最重要技術検証 — ユーザー実機確認済み）

| 検証項目 | 結果 | 備考 |
|----------|------|------|
| 非干渉モード: 付箋上クリックで背後アプリが反応 | ✅ | WM_NCHITTEST フック方式で確実 |
| 非干渉モード: フォーカスを奪わない | ✅ | WS_EX_NOACTIVATE |
| 編集モード: 付箋をクリックで操作可能 | ✅ | 三重制御の OFF で解除 |
| 編集モード: 外クリックしても編集OFFに戻らない | ✅ | 明示トグルのみ |
| トレイメニューで ON/OFF が正しくトグル | ✅ | |
| TopMost が維持されている | ✅ | 他ウィンドウで隠れない |
| AllowsTransparency=True で上記すべて成立 | ✅ | ★核心技術検証クリア |
| Alt+Tab / タスクバーに付箋が出ない | ✅ | WS_EX_TOOLWINDOW 維持 |

### 1.3 重要な技術知見（DJ-6: 三重制御方式）

**問題**: WPF `AllowsTransparency=True` は内部で `WS_EX_LAYERED` を自動付与する。この環境では `WS_EX_TRANSPARENT` の ON/OFF だけではクリック透過の切替が効かなかった。

**原因**: WS_EX_LAYERED ウィンドウの場合、WPF の内部レンダリングパイプラインがヒットテストを制御するため、Win32 の `WS_EX_TRANSPARENT` フラグ変更だけでは反映されない。

**解決策 — 三重制御方式を採用:**
1. **WS_EX_TRANSPARENT**: Win32 レベルのクリック透過
2. **WS_EX_NOACTIVATE**: フォーカスを奪わない
3. **WM_NCHITTEST フック**: `HTTRANSPARENT` を返してメッセージレベルで透過制御

```csharp
// NoteWindow.xaml.cs — WM_NCHITTEST フック（核心部分）
private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
{
    if (msg == WM_NCHITTEST && _isClickThrough)
    {
        handled = true;
        return new IntPtr(HTTRANSPARENT);  // クリックを背後のウィンドウへ通す
    }
    return IntPtr.Zero;
}
```

- **非干渉モード**: 3つすべて ON → 確実にクリック透過
- **編集モード**: WS_EX_TRANSPARENT OFF + WS_EX_NOACTIVATE OFF + WM_NCHITTEST フックが素通し → クリック可能

---

## 2. Git コミット履歴

```
1dd8ddc docs: Phase 2 完了 — TODO更新 + 進捗ログ記録 + DJ-6追加
9bdd8a7 feat: Phase 2 Win32 Interop + モード切替の実装
8c60985 chore: 一時ファイル .git_commit_msg.txt を削除
b48633d docs: 引き継ぎ資料 PART2 作成（Phase 1 完了 → Phase 2 着手へ）
bde9392 fix: トレイアイコンが表示されない問題を修正
77c2b6f docs: Phase 1 完了 — TODO更新 + 進捗ログ記録
8d7a0af feat: Phase 1 タスクトレイ常駐 + 最小付箋表示の実装
e80a0cf docs: 引き継ぎ資料 PART1 作成 + 引き継ぎ資料置き場へ移動
a8e35b6 feat: Phase 0 プロジェクト基盤の構築
```

※ 本引き継ぎ資料のコミットがこの後に続く

---

## 3. ファイル構成（現時点）

```
e:\My_Project\TopFusen\
├── TopFusen.sln
├── TopFusen/
│   ├── TopFusen.csproj              ← ApplicationIcon + Resource 追加済み
│   ├── app.manifest                 ← PerMonitorV2 + UAC=asInvoker
│   ├── App.xaml                     ← TaskbarIcon XAML リソース定義
│   ├── App.xaml.cs                  ← ★ Phase 2 更新：トレイ → NoteManager.SetEditMode() 連動
│   ├── MainWindow.xaml / .cs        ← 未使用（Visibility=Collapsed）
│   ├── AssemblyInfo.cs
│   ├── Assets/
│   │   └── app.ico                  ← トレイアイコン（32x32、付箋風）
│   ├── Models/
│   │   ├── NoteModel.cs             ← 付箋モデル（GUID, Placement, Style, MonitorIdentity）
│   │   ├── AppSettings.cs           ← アプリ設定（IsHidden, Hotkey, FontAllowList, ZOrder）
│   │   └── NotesData.cs             ← notes.json ルート
│   ├── Services/
│   │   ├── LoggingService.cs        ← Serilog 初期化
│   │   ├── SingleInstanceService.cs ← Mutex + IPC
│   │   ├── AppDataPaths.cs          ← データ保存パス管理
│   │   └── NoteManager.cs           ← ★ Phase 2 更新：IsEditMode + SetEditMode() 追加
│   ├── Views/
│   │   ├── NoteWindow.xaml          ← 付箋ウィンドウ XAML（AllowsTransparency=True）
│   │   └── NoteWindow.xaml.cs       ← ★ Phase 2 更新：三重制御方式（SetClickThrough + WM_NCHITTEST）
│   ├── ViewModels/                  ← （空、Phase 3〜 で使用）
│   └── Interop/
│       └── NativeMethods.cs         ← ★ Phase 2 更新：WS_EX_TRANSPARENT 等の定数追加
├── 引き継ぎ資料置き場/
│   ├── SESSION_HANDOVER_20260206_PART0.md
│   ├── SESSION_HANDOVER_20260206_PART1.md
│   ├── SESSION_HANDOVER_20260206_PART2.md
│   └── SESSION_HANDOVER_20260206_PART3.md ← 本ファイル
├── 要件定義.md
├── TODO.md
├── README.md
├── progress_log.txt
└── .gitignore
```

---

## 4. 現状コードの重要ポイント（引用付き）

### 4.1 NoteWindow — 三重制御方式（Views/NoteWindow.xaml.cs 全体）

クリック透過制御の核心。Phase 3 以降で WindowChrome やリサイズを追加する際、この WndProc フックとの共存に注意。

```csharp
// NoteWindow.xaml.cs:66-93 — OnSourceInitialized（HWND 生成後の初期化）
private void OnSourceInitialized(object? sender, EventArgs e)
{
    _hwnd = new WindowInteropHelper(this).Handle;
    _hwndSource = HwndSource.FromHwnd(_hwnd);

    var exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
    exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
    exStyle &= ~NativeMethods.WS_EX_APPWINDOW;

    if (_initialClickThrough)
    {
        exStyle |= NativeMethods.WS_EX_TRANSPARENT;
        exStyle |= NativeMethods.WS_EX_NOACTIVATE;
    }

    NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
    _isClickThrough = _initialClickThrough;

    // WM_NCHITTEST フック登録
    _hwndSource?.AddHook(WndProc);
}
```

```csharp
// NoteWindow.xaml.cs:101-110 — WM_NCHITTEST フック（クリック透過の主制御）
private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
{
    if (msg == WM_NCHITTEST && _isClickThrough)
    {
        handled = true;
        return new IntPtr(HTTRANSPARENT);
    }
    return IntPtr.Zero;
}
```

```csharp
// NoteWindow.xaml.cs:119-155 — SetClickThrough（モード切替）
public void SetClickThrough(bool transparent)
{
    // WS_EX_TRANSPARENT + WS_EX_NOACTIVATE を ON/OFF
    // _isClickThrough フラグを更新 → WM_NCHITTEST フックに即時反映
}
```

### 4.2 NoteManager — 編集モード管理（Services/NoteManager.cs）

```csharp
// NoteManager.cs:28-47 — 編集モード状態 + 一括切替
public bool IsEditMode { get; private set; }

public void SetEditMode(bool isEditMode)
{
    IsEditMode = isEditMode;
    var clickThrough = !isEditMode;
    foreach (var (_, window) in _notes)
    {
        window.SetClickThrough(clickThrough);
    }
}
```

```csharp
// NoteManager.cs:53-75 — 付箋作成（現在の編集モードを自動適用）
public NoteWindow CreateNote()
{
    var clickThrough = !IsEditMode;
    var window = new NoteWindow(model, clickThrough);
    // ...
}
```

### 4.3 App.xaml.cs — トレイメニュー連動

```csharp
// App.xaml.cs:111-123 — 編集モードトグル
_editModeMenuItem.Click += (_, _) =>
{
    if (_noteManager == null) return;
    var newMode = !_noteManager.IsEditMode;
    _noteManager.SetEditMode(newMode);
    _editModeMenuItem.Header = newMode
        ? "✏️ 編集モード: ON ✓"
        : "✏️ 編集モード: OFF";
};
```

### 4.4 Phase 3 で変更が必要な箇所

**Views/NoteWindow.xaml:**
- `WindowChrome` の導入（`ResizeBorderThickness` でリサイズハンドル有効化）
- `CaptionHeight=0`（カスタムタイトルバー）
- ツールバー領域（上部）+ 下部アイコン列の XAML 追加
- 選択状態による Visibility 切替

**Views/NoteWindow.xaml.cs:**
- ドラッグ移動: ツールバー領域の MouseDown で `DragMove()`
- **注意**: `WndProc` フックとの共存。WM_NCHITTEST を Phase 3 で拡張する可能性あり
  - 編集モード時に WindowChrome のリサイズ判定が正しく動くか要検証
- 選択状態管理: `IsSelected` プロパティ + UI 表示制御

**Services/NoteManager.cs:**
- 選択中付箋の管理（`SelectedNote` プロパティ）
- 付箋クリック時の選択切替
- 複製ロジック（+24px ずらし）

---

## 5. TODO 進捗状況

| Phase | 内容 | 状態 |
|-------|------|------|
| **Phase 0** | **プロジェクト基盤** | **✅ 完了 (2026-02-06)** |
| **Phase 1** | **トレイ常駐 + 最小付箋** | **✅ 完了 (2026-02-06)** |
| **Phase 2** | **Win32 Interop + モード切替** | **✅ 完了 (2026-02-06)** |
| Phase 3 | 移動・リサイズ + 基本UI | **← 次回ここから** |
| Phase 3.5 | 仮想デスクトップ技術スパイク | 未着手 |
| Phase 4 | リッチテキスト編集 | 未着手 |
| Phase 5 | 永続化 | 未着手 |
| Phase 6 | 見た目・スタイル | 未着手 |
| Phase 7 | マルチモニタ | 未着手 |
| Phase 8 | 仮想デスクトップ | 未着手 |
| Phase 9 | Z順管理 | 未着手 |
| Phase 10 | 非表示 + ホットキー + 自動起動 | 未着手 |
| Phase 11 | 設定画面 | 未着手 |
| Phase 12 | 統合テスト + ポリッシュ | 未着手 |

---

## 6. 次回対応すべきこと

### 最優先: Phase 3（付箋の移動・リサイズ + 基本UI）

1. **P3-1: WindowChrome 導入**
   - `ResizeBorderThickness` でリサイズハンドル有効化
   - `CaptionHeight=0`（カスタムタイトルバー）
   - **★注意**: AllowsTransparency=True + WindowChrome の組み合わせで、リサイズが正常に動くか要検証

2. **P3-2: ドラッグ移動実装**
   - ツールバー領域を Caption 扱い → MouseDown で `DragMove()`
   - ★ WM_NCHITTEST フックとの競合に注意。編集モード時にフックが `IntPtr.Zero` を返すので、WPF のデフォルト処理に任される → WindowChrome のリサイズ判定が動くはず

3. **P3-3: リサイズ実装**
   - 角/辺ドラッグ → WindowChrome の ResizeBorderThickness で自然に実現
   - `MinWidth=160`, `MinHeight=120` は XAML で既に設定済み

4. **P3-4: 選択状態管理**
   - 付箋クリック → 「選択中」（NoteManager で管理）
   - 選択中のみ: ツールバー + 下部アイコン + 枠/影 表示
   - 未選択: 本文のみ

5. **P3-5/P3-6: 削除・複製ボタン**
   - 下部ゴミ箱/コピーアイコン
   - 削除時 RTF ファイル掃除（Phase 5 で本格実装だが、NoteManager.DeleteNote は既にある）
   - 複製: +24px ずらし

6. **P3-7: 編集OFF時のUI非表示**
   - 編集OFF → ツールバー/アイコン/枠すべて Visibility=Collapsed

### Phase 3 実装時の注意点

- **WM_NCHITTEST フックと WindowChrome の共存**:
  - 現在 NoteWindow.WndProc は非干渉モード時のみ `HTTRANSPARENT` を返し、編集モードでは `IntPtr.Zero` を返す（デフォルト処理）
  - WindowChrome はデフォルト処理でリサイズ判定を行うので、共存は問題ないはず
  - ただし、ドラッグ移動の Caption 領域は WindowChrome の `CaptionHeight` か、`DragMove()` で実現するかを選択する必要あり
  - **もし WM_NCHITTEST でカスタム判定が必要なら、WndProc を拡張して HTCAPTION / HTLEFT / HTRIGHT 等を返す方式も検討**

- **AllowsTransparency + WindowChrome の互換性**:
  - WPF では AllowsTransparency=True + WindowChrome の組み合わせは一般的に使われるが、リサイズのエッジケースに注意
  - Phase 3-VERIFY で「AllowsTransparency + WindowChrome でリサイズが正常に動作する」を検証すること

---

## 7. 既知の注意点・リスク

1. **H.NotifyIcon.Wpf 2.1.3 の XAML 名前空間**:
   - `clr-namespace:H.NotifyIcon;assembly=H.NotifyIcon.Wpf` を使う
   - `https://hardcodet.net/wpf/NotifyIcon` は 2.1.3 では**使えない**

2. **ForceCreate() は必須**（DJ-5）:
   - XAML リソースとして TaskbarIcon を定義しても、`ForceCreate()` を呼ばないと shell に登録されない

3. **クリック透過は三重制御**（DJ-6）:
   - WS_EX_TRANSPARENT + WS_EX_NOACTIVATE + WM_NCHITTEST の3つを同時に制御する
   - WPF AllowsTransparency=True 環境では WS_EX_TRANSPARENT 単独では不十分

4. **PowerShell 環境**:
   - `&&` は使えない → `;` でコマンド連結
   - Git メッセージの日本語 → `-F ファイル` 方式を使用

5. **実行中プロセスのロック**:
   - ビルド前に TopFusen プロセスを必ず停止する
   - `Stop-Process -Name "TopFusen" -Force` で確実に止める

6. **ログレベル**:
   - 現在 MinimumLevel.Information()。Debug ログは出力されない
   - SetClickThrough のデバッグ情報は Information レベルで出力するよう変更済み

---

## 8. 設計判断ログ（累積）

- **DJ-1**: A案（複数Window）維持 — 仮想デスクトップ MoveWindowToDesktop が HWND 単位
- **DJ-2**: Z順固定ポリシー — クリックで前面化しない（Activated で再適用）
- **DJ-3**: 座標保存 — Relative主、DIP+DpiScale補助（NoteModel に反映済み）
- **DJ-4**: 仮想デスクトップ COM — UI スレッドから呼ぶ（STA COM）
- **DJ-5**: H.NotifyIcon.Wpf 2.1.3 — XAML リソース定義 + ForceCreate() が必須
- **DJ-6**: クリック透過は三重制御 — WS_EX_TRANSPARENT + WS_EX_NOACTIVATE + WM_NCHITTEST

---

## 9. NuGet パッケージ一覧

| パッケージ | バージョン | 用途 |
|-----------|-----------|------|
| H.NotifyIcon.Wpf | 2.1.3 | タスクトレイアイコン |
| Microsoft.Extensions.DependencyInjection | 8.0.1 | DI コンテナ |
| Microsoft.Extensions.Hosting | 8.0.1 | ホスティング基盤 |
| Serilog | 4.2.0 | ログ基盤 |
| Serilog.Extensions.Hosting | 8.0.0 | Serilog ホスト統合 |
| Serilog.Sinks.File | 6.0.0 | ファイル出力シンク |

---

## 10. 次の枠で最初にやること

> **以下のファイルを読んでから作業を開始してください:**
>
> 1. `要件定義.md` — PRD v0.2.0 全文
> 2. `TODO.md` — 全体実装計画（Phase 0〜2 完了、Phase 3 から着手）
> 3. `引き継ぎ資料置き場/SESSION_HANDOVER_20260206_PART3.md` — 本ファイル
>
> 読み終わったら **Phase 3 の P3-1（WindowChrome 導入）** から実装を開始。
