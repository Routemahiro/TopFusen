# SESSION_HANDOVER_20260206_PART2 — Phase 1 実装完了

> 作成日: 2026-02-06
> セッション種別: 実装（Phase 1: タスクトレイ常駐 + 最小付箋表示）
> 前回: SESSION_HANDOVER_20260206_PART1（Phase 0 完了）
> 次回: Phase 2（Win32 Interop + モード切替）から開始

---

## 1. 今回実施した内容

### 1.1 Phase 1: タスクトレイ常駐 + 最小付箋表示 — 全5タスク完了

| タスク | 内容 | 結果 |
|--------|------|------|
| P1-1 | タスクトレイアイコン実装（app.ico + H.NotifyIcon.Wpf） | ✅ |
| P1-2 | トレイ右クリックメニュー骨格（5項目） | ✅ |
| P1-3 | NoteWindow 基本実装（Borderless + Topmost） | ✅ |
| P1-4 | NoteManager 骨格（生成/保持/破棄） | ✅ |
| P1-5 | Alt+Tab / タスクバー非表示（WS_EX_TOOLWINDOW） | ✅ |

### 1.2 P1-VERIFY 検証結果（ユーザー実機確認済み）

| 検証項目 | 結果 | 備考 |
|----------|------|------|
| トレイアイコン表示 | ✅ | ForceCreate() 必須（後述の注意点参照） |
| 右クリックメニュー | ✅ | 5項目すべて表示 |
| 新規付箋作成 | ✅ | 黄色い付箋が画面中央に出現 |
| 終了 | ✅ | プロセス完全終了 |
| Topmost | ✅ | 他ウィンドウより前面 |
| Alt+Tab 非表示 | ✅ | WS_EX_TOOLWINDOW で隠れている |
| タスクバー非表示 | ✅ | ShowInTaskbar=False |

### 1.3 トレイアイコン表示問題と修正（重要な知見）

**問題**: H.NotifyIcon.Wpf 2.1.3 で `TaskbarIcon` をコードビハインドのみで生成し `IconSource` を設定しても、トレイにアイコンが表示されなかった。

**原因**: コードビハインドだけの TaskbarIcon 生成では、shell notification icon の作成が自動では走らない。

**解決策（公式サンプル準拠）**:
1. `App.xaml` で `TaskbarIcon` を **XAML リソースとして定義**
2. XAML 名前空間: `clr-namespace:H.NotifyIcon;assembly=H.NotifyIcon.Wpf`
   - ※ 新しいバージョンの `https://hardcodet.net/wpf/NotifyIcon` は **2.1.3 では使えない**
3. コードビハインドで `FindResource("TrayIcon")` → ContextMenu 設定 → **`ForceCreate()`** 呼び出し

```csharp
// App.xaml.cs — 正しいパターン
_trayIcon = (TaskbarIcon)FindResource("TrayIcon");
_trayIcon.ContextMenu = CreateTrayContextMenu();
_trayIcon.ForceCreate(); // これが必須！
```

```xml
<!-- App.xaml — XAML リソースとして定義 -->
<tb:TaskbarIcon x:Key="TrayIcon"
                IconSource="/Assets/app.ico"
                ToolTipText="TopFusen — 付箋オーバーレイ" />
```

---

## 2. Git コミット履歴

```
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
│   ├── App.xaml                     ← TaskbarIcon XAML リソース定義（Phase 1）
│   ├── App.xaml.cs                  ← トレイ初期化 + NoteManager 統合（Phase 1）
│   ├── MainWindow.xaml / .cs        ← 未使用（Visibility=Collapsed）
│   ├── AssemblyInfo.cs
│   ├── Assets/
│   │   └── app.ico                  ← トレイアイコン（32x32、付箋風）
│   ├── Models/
│   │   ├── NoteModel.cs             ← 付箋モデル
│   │   ├── AppSettings.cs           ← アプリ設定
│   │   └── NotesData.cs             ← notes.json ルート
│   ├── Services/
│   │   ├── LoggingService.cs        ← Serilog 初期化
│   │   ├── SingleInstanceService.cs ← Mutex + IPC
│   │   ├── AppDataPaths.cs          ← データ保存パス管理
│   │   └── NoteManager.cs           ← ★ Phase 1 新規：付箋ライフサイクル管理
│   ├── Views/
│   │   ├── NoteWindow.xaml          ← ★ Phase 1 新規：付箋ウィンドウ XAML
│   │   └── NoteWindow.xaml.cs       ← ★ Phase 1 新規：WS_EX_TOOLWINDOW 適用
│   ├── ViewModels/                  ← （空、Phase 3〜 で使用）
│   └── Interop/
│       └── NativeMethods.cs         ← ★ Phase 1 新規：Win32 P/Invoke 最小限
├── 引き継ぎ資料置き場/
│   ├── SESSION_HANDOVER_20260206_PART0.md
│   ├── SESSION_HANDOVER_20260206_PART1.md
│   └── SESSION_HANDOVER_20260206_PART2.md ← 本ファイル
├── 要件定義.md
├── TODO.md
├── README.md
├── progress_log.txt
└── .gitignore
```

---

## 4. 現状コードの重要ポイント（引用付き）

### 4.1 アプリ起動フロー（App.xaml.cs）

```csharp
// App.xaml.cs:34-76 — OnStartup の流れ
// 1. LoggingService.Initialize()     — ログ基盤
// 2. AppDataPaths.EnsureDirectories() — データディレクトリ作成
// 3. SingleInstanceService.TryAcquire() — Mutex排他
// 4. DI コンテナ構築
// 5. SessionEnding フック登録
// 6. NoteManager 初期化（DI から取得）  ← Phase 1 追加
// 7. InitializeTrayIcon()             ← Phase 1 追加
```

### 4.2 トレイアイコン初期化（App.xaml.cs:92-102）

```csharp
private void InitializeTrayIcon()
{
    _trayIcon = (TaskbarIcon)FindResource("TrayIcon"); // XAML リソースから取得
    _trayIcon.ContextMenu = CreateTrayContextMenu();
    _trayIcon.ForceCreate(); // shell icon 作成（必須！）
}
```

### 4.3 NoteWindow の Win32 拡張スタイル適用（Views/NoteWindow.xaml.cs）

```csharp
// SourceInitialized イベントで HWND 生成後に適用
private void OnSourceInitialized(object? sender, EventArgs e)
{
    var hwnd = new WindowInteropHelper(this).Handle;
    var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
    exStyle |= NativeMethods.WS_EX_TOOLWINDOW;   // Alt+Tab 非表示
    exStyle &= ~NativeMethods.WS_EX_APPWINDOW;   // タスクバー非表示
    NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
}
```

### 4.4 Phase 2 で変更が必要な箇所

**Interop/NativeMethods.cs:**
- `WS_EX_TRANSPARENT` (0x00000020) を追加 — クリック透過
- `WS_EX_LAYERED` (0x00080000) を追加 — AllowsTransparency 用（WPF が自動で付けるが確認用）
- `WS_EX_NOACTIVATE` (0x08000000) を追加 — フォーカス奪わない

**Views/NoteWindow.xaml.cs:**
- `SetClickThrough(bool transparent)` メソッド追加
  - transparent=true: WS_EX_TRANSPARENT + WS_EX_NOACTIVATE ON
  - transparent=false: WS_EX_TRANSPARENT OFF

**App.xaml.cs:**
- `_isEditMode` フィールド（既存）をモード切替メソッドに連動
- 全 NoteWindow に一括で `SetClickThrough()` を呼ぶ
- トレイメニューの「編集モード」トグルと連動

### 4.5 終了処理（App.xaml.cs:197-216）

```csharp
protected override void OnExit(ExitEventArgs e)
{
    // TODO: Phase 5 で永続化フラッシュ保存
    _noteManager?.CloseAllWindows();  // 全付箋閉じる
    _trayIcon?.Dispose();             // トレイアイコン破棄
    _singleInstance?.Dispose();       // Mutex 解放
    _serviceProvider?.Dispose();      // DI コンテナ破棄
    LoggingService.Shutdown();        // ログ最終出力
}
```

---

## 5. TODO 進捗状況

| Phase | 内容 | 状態 |
|-------|------|------|
| **Phase 0** | **プロジェクト基盤** | **✅ 完了 (2026-02-06)** |
| **Phase 1** | **トレイ常駐 + 最小付箋** | **✅ 完了 (2026-02-06)** |
| Phase 2 | Win32 Interop + モード切替 | **← 次回ここから** |
| Phase 3 | 移動・リサイズ + 基本UI | 未着手 |
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

### 最優先: Phase 2（Win32 Interop + モード切替）★最重要技術検証

> **TopFusen の核心**: AllowsTransparency + WS_EX_TRANSPARENT + TopMost + WS_EX_TOOLWINDOW の共存確認

1. **P2-1: Win32 Interop ヘルパー拡張**
   - 既存の `Interop/NativeMethods.cs` に定数追加
   - WS_EX_TRANSPARENT / WS_EX_LAYERED / WS_EX_NOACTIVATE

2. **P2-2: NoteWindow にクリック透過の ON/OFF 実装**
   - 非干渉モード: WS_EX_TRANSPARENT + WS_EX_NOACTIVATE ON
   - 編集モード: WS_EX_TRANSPARENT OFF
   - `SetClickThrough(bool)` メソッドを NoteWindow に追加

3. **P2-3: AppHost にモード管理（IsEditMode）**
   - トレイメニューのトグルと連動
   - 全 NoteWindow に一括適用

4. **P2-VERIFY: 検証（★最重要）**
   - 非干渉モード: クリック透過で背後アプリが反応する
   - 非干渉モード: フォーカスを奪わない
   - 編集モード: 付箋をクリックで操作可能
   - 編集モード: 外クリックしても編集OFFに戻らない
   - TopMost 維持
   - Alt+Tab / タスクバー非表示が維持
   - AllowsTransparency=True との共存

---

## 7. 既知の注意点・リスク

1. **H.NotifyIcon.Wpf 2.1.3 の XAML 名前空間**:
   - `clr-namespace:H.NotifyIcon;assembly=H.NotifyIcon.Wpf` を使う
   - `https://hardcodet.net/wpf/NotifyIcon` は 2.1.3 では**使えない**

2. **ForceCreate() は必須**:
   - XAML リソースとして TaskbarIcon を定義しても、`ForceCreate()` を呼ばないと shell に登録されない

3. **PowerShell 環境**:
   - `&&` は使えない → `;` でコマンド連結
   - Git メッセージの日本語 → `-F ファイル` 方式を使用

4. **Phase 2 が最重要技術検証**:
   - AllowsTransparency + WS_EX_TRANSPARENT + TopMost + WS_EX_TOOLWINDOW の全共存
   - ここが崩れるとアーキテクチャ見直しが必要

5. **実行中プロセスのロック**:
   - ビルド前に TopFusen プロセスを必ず停止する
   - `Stop-Process -Name "TopFusen" -Force` で確実に止める

---

## 8. 設計判断ログ（累積）

- **DJ-1**: A案（複数Window）維持 — 仮想デスクトップ MoveWindowToDesktop が HWND 単位
- **DJ-2**: Z順固定ポリシー — クリックで前面化しない（Activated で再適用）
- **DJ-3**: 座標保存 — Relative主、DIP+DpiScale補助（NoteModel に反映済み）
- **DJ-4**: 仮想デスクトップ COM — UI スレッドから呼ぶ（STA COM）
- **DJ-5**: H.NotifyIcon.Wpf 2.1.3 — XAML リソース定義 + ForceCreate() が必須（Phase 1 で判明）

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
> 2. `TODO.md` — 全体実装計画（Phase 0〜1 完了、Phase 2 から着手）
> 3. `引き継ぎ資料置き場/SESSION_HANDOVER_20260206_PART2.md` — 本ファイル
>
> 読み終わったら **Phase 2 の P2-1** から実装を開始。
