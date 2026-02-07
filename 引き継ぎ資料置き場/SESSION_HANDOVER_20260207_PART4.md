# SESSION_HANDOVER_20260207_PART4 — Phase 3 + Phase 3.5 完了 + DJ-7 対応指示

> 作成日: 2026-02-07
> セッション種別: 実装（Phase 3: 移動・リサイズ + Phase 3.5: 仮想デスクトップスパイク）
> 前回: SESSION_HANDOVER_20260206_PART3（Phase 2 完了）
> **次回最優先: DJ-7 対応（WS_EX_TOOLWINDOW → オーナーウィンドウ方式変更）、その後 Phase 4 着手**

---

## 1. 今回実施した内容

### 1.1 Phase 3: 付箋の移動・リサイズ + 基本UI — 全7タスク + 検証完了

実装方針: **案B（WindowChrome + DragMove + 部分バインディング）**

| タスク | 内容 | 結果 |
|--------|------|------|
| P3-1 | WindowChrome 導入（ResizeBorderThickness=6, CaptionHeight=0） | ✅ |
| P3-2 | ツールバー領域ドラッグで DragMove 移動 | ✅ |
| P3-3 | WindowChrome による角/辺リサイズ（MinWidth=160, MinHeight=120） | ✅ |
| P3-4 | 選択状態管理（IsSelected + IsInEditMode + UpdateVisualState） | ✅ |
| P3-5 | 削除ボタン（🗑）→ NoteManager.DeleteNote 連携 | ✅ |
| P3-6 | 複製ボタン（📋）→ +24px ずらし + スタイルコピー + クランプ | ✅ |
| P3-7 | 編集OFF時に全UI要素非表示（本文のみ表示） | ✅ |

### 1.2 Phase 3.5: 仮想デスクトップ技術スパイク — 全4タスク + 検証完了

| タスク | 内容 | 結果 |
|--------|------|------|
| P3.5-1 | IVirtualDesktopManager COM 初期化 | ✅ 成功 |
| P3.5-2 | 現在デスクトップID取得（短命ウィンドウ方式） | ✅ 成功 |
| P3.5-3 | MoveWindowToDesktop テスト | ⚠️ 普通Window成功、NoteWindow失敗（DJ-7） |
| P3.5-4 | Registry からデスクトップ一覧取得 | ✅ 成功 |

### 1.3 重要な発見: DJ-7（WS_EX_TOOLWINDOW 問題）

**問題:**
`WS_EX_TOOLWINDOW`（オーナーなし）のウィンドウは、OS の仮想デスクトップ管理に参加しない。
- `GetWindowDesktopId` が `Guid.Empty` を返す（OS がウィンドウを追跡していない）
- `MoveWindowToDesktop` は `HRESULT=0`（成功）を返すが、実際には移動しない
- 普通の Window（TOOLWINDOW なし）では問題なく移動できることを確認済み

**対策（次回最優先）:**
Alt+Tab 非表示を **オーナーウィンドウ方式** に変更する：
1. 非表示のオーナー Window を1つ作成
2. NoteWindow の Owner に設定（オーナー付きウィンドウは Alt+Tab に出ない）
3. WS_EX_TOOLWINDOW を除去
4. MoveWindowToDesktop が正常動作することを確認

---

## 2. Git コミット履歴（本セッション分）

```
33cdb83 docs: Phase 3.5 完了 — TODO更新 + 進捗ログ + DJ-7追加
af967d2 feat: Phase 3.5 仮想デスクトップ技術スパイク実装
87e233a docs: Phase 3.5 作業開始（案B: 全API検証 + VirtualDesktopService分離）
ce3147c docs: Phase 3 完了 — TODO更新 + 進捗ログ記録
02e22a7 feat: Phase 3 付箋の移動・リサイズ + 基本UI実装
f42752d docs: Phase 3 作業開始（案B: WindowChrome + DragMove + 部分バインディング）
```

---

## 3. ファイル構成（現時点）

```
e:\My_Project\TopFusen\
├── TopFusen.sln
├── TopFusen/
│   ├── TopFusen.csproj
│   ├── app.manifest                 ← PerMonitorV2 + UAC=asInvoker
│   ├── App.xaml                     ← TaskbarIcon XAML リソース定義
│   ├── App.xaml.cs                  ← ★ Phase 3.5: VDサービス初期化 + スパイク検証メニュー
│   ├── MainWindow.xaml / .cs        ← 未使用（Visibility=Collapsed）
│   ├── AssemblyInfo.cs
│   ├── Assets/
│   │   └── app.ico
│   ├── Models/
│   │   ├── NoteModel.cs
│   │   ├── AppSettings.cs
│   │   └── NotesData.cs
│   ├── Services/
│   │   ├── LoggingService.cs
│   │   ├── SingleInstanceService.cs
│   │   ├── AppDataPaths.cs
│   │   ├── NoteManager.cs           ← ★ Phase 3: 選択管理 + 複製 + イベント連携
│   │   └── VirtualDesktopService.cs ← ★ NEW: COM初期化 + 短命Window + Registry + Move
│   ├── Views/
│   │   ├── NoteWindow.xaml          ← ★ Phase 3: WindowChrome + ツールバー + 下部アイコン
│   │   └── NoteWindow.xaml.cs       ← ★ Phase 3: 選択状態 + DragMove + イベント
│   ├── ViewModels/                  ← （空、Phase 4〜 で使用）
│   └── Interop/
│       ├── NativeMethods.cs
│       └── VirtualDesktopInterop.cs ← ★ NEW: IVirtualDesktopManager COM 定義
├── 引き継ぎ資料置き場/
│   ├── SESSION_HANDOVER_20260206_PART0〜3.md
│   └── SESSION_HANDOVER_20260207_PART4.md ← 本ファイル
├── 要件定義.md
├── TODO.md
├── README.md
├── progress_log.txt
└── .gitignore
```

---

## 4. 次回最優先: DJ-7 対応（WS_EX_TOOLWINDOW → オーナーウィンドウ方式）

### 4.1 なぜ Phase 4 より先にやるのか

- Phase 4〜7 で NoteWindow を大量に改修する
- Phase 8 で基盤（Alt+Tab 非表示方式）を変えると、Phase 4〜7 すべてに手戻りが発生するリスク
- **基盤変更は早い段階で確定させるのが安全**
- 変更量自体は小さい（NoteWindow の HWND スタイル設定部分 + オーナーウィンドウ生成のみ）

### 4.2 実装手順

**TODO.md の Phase 順序を整理してから着手すること。** 例: Phase 3.7 として挿入するか、Phase 3.5 の延長として追記。

1. **オーナーウィンドウの作成**
   - App.xaml.cs または NoteManager で、非表示の Window を1つ作成（アプリ起動時）
   - `Width=0, Height=0, WindowStyle=None, ShowInTaskbar=false, ShowActivated=false, Visibility=Hidden`
   - これを全 NoteWindow の `Owner` に設定

2. **NoteWindow から WS_EX_TOOLWINDOW を除去**
   - `NoteWindow.xaml.cs` の `OnSourceInitialized` で `WS_EX_TOOLWINDOW` を付与していた部分を削除
   - `ShowInTaskbar=false` はそのまま維持（Owner がある場合はタスクバーに出ない）

3. **検証**
   - Alt+Tab に付箋が出ないことを確認
   - タスクバーに付箋が出ないことを確認
   - `GetWindowDesktopId` が有効な GUID を返すことを確認
   - `MoveWindowToDesktop` が実際に付箋を移動することを確認
   - クリック透過（三重制御）が引き続き正常に動作することを確認
   - Topmost が維持されていることを確認

4. **スパイク検証メニューで再テスト**
   - 「🔬 VD: 付箋移動テスト」で NoteWindow が別デスクトップへ移動することを確認

### 4.3 注意点

- **WPF Owner の挙動**: Owner が Hide/Close されると子ウィンドウも影響を受ける。オーナーは最後まで生かすこと
- **WPF Owner + Topmost**: Owner 付き Window でも `Topmost=True` は有効なはず。ただし要検証
- **WPF Owner + AllowsTransparency**: 組み合わせの問題がないか要検証
- **フォーカス挙動**: オーナー付きになることで、NoteWindow のフォーカス挙動が変わる可能性（Activated イベントの発火タイミングなど）

### 4.4 変更が必要なファイル

| ファイル | 変更内容 |
|---------|---------|
| `App.xaml.cs` または `NoteManager.cs` | オーナーウィンドウの生成・管理 |
| `Views/NoteWindow.xaml.cs` | `OnSourceInitialized` から WS_EX_TOOLWINDOW 付与を削除。Owner を受け取る |
| `Views/NoteWindow.xaml` | 変更なし（Owner は XAML ではなくコードで設定） |

---

## 5. DJ-7 対応後の作業: Phase 4（リッチテキスト編集）

DJ-7 対応が完了したら、通常のロードマップどおり Phase 4 に進む：
- P4-1: NoteWindow に WPF RichTextBox 配置
- P4-2: ツールバー実装（太字/下線/取り消し線/文字サイズ/文字色）
- P4-3〜P4-6: 適用ルール、ツールチップ、Undo/Redo、クリップボード

---

## 6. TODO 進捗状況

| Phase | 内容 | 状態 |
|-------|------|------|
| Phase 0 | プロジェクト基盤 | ✅ 完了 (2026-02-06) |
| Phase 1 | トレイ常駐 + 最小付箋 | ✅ 完了 (2026-02-06) |
| Phase 2 | Win32 Interop + モード切替 | ✅ 完了 (2026-02-06) |
| Phase 3 | 移動・リサイズ + 基本UI | ✅ 完了 (2026-02-07) |
| Phase 3.5 | 仮想デスクトップ技術スパイク | ✅ 完了 (2026-02-07) |
| **DJ-7 対応** | **WS_EX_TOOLWINDOW → オーナーウィンドウ方式** | **← 次回最優先** |
| Phase 4 | リッチテキスト編集 | 未着手 |
| Phase 5 | 永続化 | 未着手 |
| Phase 6〜12 | （後続フェーズ） | 未着手 |

---

## 7. 設計判断ログ（累積）

- **DJ-1**: A案（複数Window）維持 — 仮想デスクトップ MoveWindowToDesktop が HWND 単位
- **DJ-2**: Z順固定ポリシー — クリックで前面化しない（Activated で再適用）
- **DJ-3**: 座標保存 — Relative主、DIP+DpiScale補助
- **DJ-4**: 仮想デスクトップ COM — UI スレッドから呼ぶ（STA COM）
- **DJ-5**: H.NotifyIcon.Wpf 2.1.3 — XAML リソース定義 + ForceCreate() が必須
- **DJ-6**: クリック透過は三重制御 — WS_EX_TRANSPARENT + WS_EX_NOACTIVATE + WM_NCHITTEST
- **DJ-7**: WS_EX_TOOLWINDOW は仮想デスクトップ管理対象外 → オーナーウィンドウ方式に変更

---

## 8. 既知の注意点

1. **H.NotifyIcon.Wpf のトレイメニューから MessageBox を出すと一瞬で消える**
   - `async + await Task.Delay(300)` でメニュー閉じ待ちが必要
2. **PowerShell で日本語コミットメッセージ**: `-F ファイル` 方式を使う
3. **実行中プロセスのロック**: ビルド前に `Stop-Process -Name "TopFusen" -Force` で停止
4. **クリック透過は三重制御**（DJ-6）: WM_NCHITTEST フックとの共存に注意
5. **WindowChrome + AllowsTransparency=True**: `.NET 8 WPF で正常動作を確認済み。ただし UseAeroPeek プロパティは存在しない`

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
> 2. `TODO.md` — 全体実装計画
> 3. `引き継ぎ資料置き場/SESSION_HANDOVER_20260207_PART4.md` — 本ファイル
>
> **読み終わったら:**
> 1. **TODO.md の Phase 順序を整理**（DJ-7 対応を Phase 3.5 の後に挿入）
> 2. **DJ-7 対応を実施**（セクション 4 の手順に従う）
> 3. **検証完了後、Phase 4（リッチテキスト編集）** に着手
