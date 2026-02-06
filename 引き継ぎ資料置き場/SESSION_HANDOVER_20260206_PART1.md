# SESSION_HANDOVER_20260206_PART1 — Phase 0 実装完了

> 作成日: 2026-02-06
> セッション種別: 実装（Phase 0: プロジェクト基盤）
> 前回: SESSION_HANDOVER_20260206_PART0（計画策定）
> 次回: Phase 1（タスクトレイ常駐 + 最小付箋表示）から開始

---

## 1. 今回実施した内容

### 1.1 Phase 0: プロジェクト基盤 — 全10タスク完了

| タスク | 内容 | 結果 |
|--------|------|------|
| P0-1 | .NET 8 WPF ソリューション作成 + フォルダ構成 | ✅ |
| P0-2 | app.manifest（PerMonitorV2 + UAC=asInvoker） | ✅ |
| P0-3 | App.xaml ShutdownMode=OnExplicitShutdown | ✅ |
| P0-4 | DI コンテナ導入（Microsoft.Extensions.DependencyInjection） | ✅ |
| P0-5 | Serilog ログ基盤（7日ローテーション） | ✅ |
| P0-6 | 設定モデル定義（NoteModel / AppSettings / NotesData） | ✅ |
| P0-7 | .gitignore 作成 | ✅ |
| P0-8 | README.md 作成 | ✅ |
| P0-9 | 単一インスタンス制御（Mutex + NamedPipe IPC） | ✅ |
| P0-10 | トレイ実装方式確定 → **H.NotifyIcon.Wpf 2.1.3** | ✅ |

### 1.2 P0-VERIFY 検証結果

| 検証項目 | 結果 | 備考 |
|----------|------|------|
| ビルド（エラー0、警告0） | ✅ | `dotnet build` 成功 |
| アプリ起動（OnExplicitShutdown で常駐） | ✅ | プロセス常駐確認 |
| ログファイル生成 | ✅ | `app_20260206.log` に7行出力 |
| 二重起動排他 | ✅ | 2番目が即終了 + IPC コマンド送受信成功 |

### 1.3 トレイライブラリ選定の経緯（P0-10）

- **H.NotifyIcon.Wpf 2.4.1**: net8.0-windows10.0.17763 に対して NU1701 警告（.NET Framework フォールバック）
- **H.NotifyIcon.Wpf 2.1.3**: net8.0-windows10.0.17763 と完全互換。**こちらを採用**
- Phase 1 でトレイアイコン実装時にこのパッケージを使用する

---

## 2. Git コミット履歴

```
a8e35b6 feat: Phase 0 プロジェクト基盤の構築
aa1b0b2 docs: 引き継ぎ資料 PART0（計画策定セッション）を作成
dda966a docs: PRD v0.2.0 要件定義 + TODO 全体実装計画を作成
```

※ 本引き継ぎ資料のコミットがこの後に続く

---

## 3. ファイル構成（現時点）

```
e:\My_Project\TopFusen\
├── TopFusen.sln                          ← ソリューションファイル
├── TopFusen/                             ← WPF アプリケーション
│   ├── TopFusen.csproj                   ← net8.0-windows10.0.17763.0
│   ├── app.manifest                      ← PerMonitorV2 + UAC=asInvoker
│   ├── App.xaml                          ← ShutdownMode=OnExplicitShutdown
│   ├── App.xaml.cs                       ← エントリポイント（DI/ログ/単一インスタンス）
│   ├── MainWindow.xaml / .cs             ← Phase 0 では未使用（Visibility=Collapsed）
│   ├── AssemblyInfo.cs
│   ├── Models/
│   │   ├── NoteModel.cs                  ← 付箋モデル（NoteId, Placement, Style等）
│   │   ├── AppSettings.cs                ← アプリ設定（IsHidden, Hotkey, FontAllowList等）
│   │   └── NotesData.cs                  ← notes.json ルートオブジェクト
│   ├── Services/
│   │   ├── LoggingService.cs             ← Serilog 初期化/シャットダウン
│   │   ├── SingleInstanceService.cs      ← Mutex + NamedPipe IPC
│   │   └── AppDataPaths.cs              ← データ保存パスの一元管理
│   ├── Views/                            ← （空、Phase 3〜 で使用）
│   ├── ViewModels/                       ← （空、Phase 3〜 で使用）
│   ├── Interop/                          ← （空、Phase 2 で Win32 ヘルパー作成）
│   └── Assets/                           ← （空、Phase 1 でアイコン配置）
├── 引き継ぎ資料置き場/
│   ├── SESSION_HANDOVER_20260206_PART0.md
│   └── SESSION_HANDOVER_20260206_PART1.md ← 本ファイル
├── 要件定義.md                            ← PRD v0.2.0 全文
├── TODO.md                               ← 全体実装計画（Phase 0 完了済み）
├── README.md                             ← プロジェクト概要 + ビルド手順
├── progress_log.txt                      ← 実装進捗ログ
└── .gitignore
```

---

## 4. NuGet パッケージ一覧

| パッケージ | バージョン | 用途 |
|-----------|-----------|------|
| H.NotifyIcon.Wpf | 2.1.3 | タスクトレイアイコン（Phase 1 で使用） |
| Microsoft.Extensions.DependencyInjection | 8.0.1 | DI コンテナ |
| Microsoft.Extensions.Hosting | 8.0.1 | ホスティング基盤 |
| Serilog | 4.2.0 | ログ基盤 |
| Serilog.Extensions.Hosting | 8.0.0 | Serilog ホスト統合 |
| Serilog.Sinks.File | 6.0.0 | ファイル出力シンク |

---

## 5. 現状コードの重要ポイント（引用付き）

### 5.1 アプリ起動フロー（App.xaml.cs）

起動時の処理順序:
1. Serilog 初期化（`LoggingService.Initialize()`）
2. データディレクトリ作成（`AppDataPaths.EnsureDirectories()`）
3. 単一インスタンスチェック（`SingleInstanceService.TryAcquire()`）
4. DI コンテナ構築
5. SessionEnding フック登録

```csharp
// App.xaml.cs:26-65 — OnStartup
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    LoggingService.Initialize();
    AppDataPaths.EnsureDirectories();
    _singleInstance = new SingleInstanceService();
    if (!_singleInstance.TryAcquire())
    {
        Shutdown(0);
        return;
    }
    // ... DI構築, SessionEndingフック ...
}
```

### 5.2 Phase 1 で変更が必要な箇所

**App.xaml.cs の `ConfigureServices`（70行目付近）:**
```csharp
private static void ConfigureServices(IServiceCollection services)
{
    services.AddSingleton<SingleInstanceService>();
    // TODO: Phase 1 以降で NoteManager, PersistenceService 等を追加
}
```
→ Phase 1 で `NoteManager` や トレイアイコンサービスを DI 登録する

**App.xaml.cs の `OnStartup` 末尾（61行目付近）:**
→ Phase 1 でトレイアイコンを初期化するコードを追加する

**App.xaml.cs の `OnIpcCommandReceived`（81行目付近）:**
```csharp
case "SHOW_SETTINGS":
    // TODO: Phase 11 で設定画面を前面に出す
    break;
```

**App.xaml.cs の `OnSessionEnding`（103行目付近）:**
→ Phase 5 で永続化のフラッシュ保存を追加する

### 5.3 モデル定義のポイント

**NoteModel.cs — DJ-3（座標保存ルール）反映済み:**
- `NotePlacement.RelativeX/Y` — WorkArea 基準の相対座標（0.0〜1.0）が主
- `NotePlacement.DipX/Y` — DIP 座標が補助
- `NotePlacement.DpiScale` — 保存時の DPI スケール

**AppSettings.cs — ホットキー既定値:**
- `Modifiers = 0x0003`（MOD_CONTROL | MOD_WIN）
- `Key = 0x45`（'E'）
- → Ctrl+Win+E

---

## 6. TODO 進捗状況

| Phase | 内容 | 状態 |
|-------|------|------|
| **Phase 0** | **プロジェクト基盤** | **✅ 完了 (2026-02-06)** |
| Phase 1 | トレイ常駐 + 最小付箋 | **← 次回ここから** |
| Phase 2 | Win32 Interop + モード切替 | 未着手 |
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
| Phase 12 | 統合テスト + 回帰テスト + ポリッシュ | 未着手 |

---

## 7. 次回対応すべきこと

### 最優先: Phase 1（タスクトレイ常駐 + 最小付箋表示）

1. **P1-1: タスクトレイアイコン実装**
   - H.NotifyIcon.Wpf 2.1.3 を使用
   - アイコンリソース（.ico）を `Assets/` に配置
   - `TopFusen.csproj` の `ApplicationIcon` コメントを有効化

2. **P1-2: トレイ右クリックメニュー骨格**
   - 編集モード ON/OFF（トグル）
   - 新規付箋作成
   - 一時的に非表示（トグル）
   - 設定を開く（stub）
   - 終了

3. **P1-3: NoteWindow 基本実装（Borderless WPF Window）**
   - WindowStyle=None, AllowsTransparency=True, Topmost=True
   - ShowInTaskbar=False
   - 最小サイズ 160×120
   - 背景色つき矩形として表示

4. **P1-4: NoteManager 骨格**
   - 付箋の生成/保持/破棄

5. **P1-5: NoteWindow を Alt+Tab / タスクバーから隠す**
   - WS_EX_TOOLWINDOW 付与（Win32 interop 最小限先行実装）
   - WS_EX_APPWINDOW を外す

### Phase 1 実装の注意点

- **MainWindow.xaml は Phase 1 で不要化** — トレイ常駐のみになるので削除 or 空のまま
- **App.xaml の StartupUri が無い** — 既に削除済み（OnExplicitShutdown のため）OK
- **H.NotifyIcon.Wpf の使い方**: XAML で `<tb:TaskbarIcon>` を定義するか、コードビハインドで生成
- **「終了」メニュー**: `Application.Current.Shutdown()` を呼ぶ。OnExit が発火する

---

## 8. 既知の注意点・リスク

1. **PowerShell 環境**: `&&` 演算子が使えない。コマンド連結は `;` を使用。Git コミットメッセージに日本語を含む場合は `-F ファイル` 方式を使う（heredoc不可）
2. **Phase 2 が最重要技術検証**: AllowsTransparency + WS_EX_TRANSPARENT + TopMost + WindowChrome の共存。Phase 1 で AllowsTransparency=True を先行設定するので、Phase 2 で共存テストを行う
3. **Phase 3.5（仮想デスクトップスパイク）**: COM 初期化失敗時の graceful 無効化パスを忘れずに
4. **アイコンファイル未作成**: Phase 1 で .ico ファイルを作成する必要あり。仮アイコンでもOK
5. **ImplicitUsings の注意**: WPF プロジェクトの ImplicitUsings には `System.IO` が含まれない。各ファイルで明示的に `using System.IO;` を追加する必要あり

---

## 9. 設計判断ログ（前回 PART0 から引き継ぎ）

- **DJ-1**: A案（複数Window）維持 — 仮想デスクトップ MoveWindowToDesktop が HWND 単位
- **DJ-2**: Z順固定ポリシー — クリックで前面化しない（Activated で再適用）
- **DJ-3**: 座標保存 — Relative主、DIP+DpiScale補助（NoteModel に反映済み）
- **DJ-4**: 仮想デスクトップ COM — UI スレッドから呼ぶ（STA COM）

---

## 10. 次の枠で最初にやること

> **以下のファイルを読んでから作業を開始してください:**
>
> 1. `要件定義.md` — PRD v0.2.0 全文
> 2. `TODO.md` — 全体実装計画（Phase 0 完了、Phase 1 から着手）
> 3. `引き継ぎ資料置き場/SESSION_HANDOVER_20260206_PART1.md` — 本ファイル
>
> 読み終わったら **Phase 1 の P1-1** から実装を開始。
