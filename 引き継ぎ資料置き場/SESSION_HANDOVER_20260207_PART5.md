# SESSION_HANDOVER_20260207_PART5 — Phase 3.7（DJ-7/DJ-8）完了 → Phase 4 着手へ

> 作成日: 2026-02-07
> セッション種別: 実装（Phase 3.7: DJ-7/DJ-8 対応 — WS_EX_TOOLWINDOW → オーナーウィンドウ方式）
> 前回: SESSION_HANDOVER_20260207_PART4（Phase 3 + Phase 3.5 完了）
> **次回最優先: Phase 4（リッチテキスト編集）着手**

---

## 1. 今回実施した内容

### 1.1 Phase 3.7: DJ-7/DJ-8 対応 — 全タスク + 検証完了

| タスク | 内容 | 結果 |
|--------|------|------|
| P3.7-1 | NoteManager にオーナーウィンドウ生成・管理を追加 | ✅ |
| P3.7-2 | NoteWindow から WS_EX_TOOLWINDOW を除去 | ✅ |
| DJ-8 修正 | ウィンドウ生成時のクリック透過を Show() 後に遅延適用 | ✅ |
| DJ-8 修正2 | IsInEditMode=false 固定初期化（UI要素表示バグ解消） | ✅ |
| P3.7-VERIFY | 全9項目の検証（実機確認済み） | ✅ |

### 1.2 DJ-7: WS_EX_TOOLWINDOW → オーナーウィンドウ方式

**問題**: `WS_EX_TOOLWINDOW`（オーナーなし）のウィンドウは OS の仮想デスクトップ管理に参加しない。

**対策**:
1. NoteManager に非表示のオーナー Window を1つ作成（`EnsureHandle()` で HWND 確保）
2. 全 NoteWindow の `Owner` に設定 → オーナー付きウィンドウは Alt+Tab に出ない
3. NoteWindow から `WS_EX_TOOLWINDOW` 付与コードを除去
4. `MoveWindowToDesktop` が正常に動作するようになった

### 1.3 DJ-8: クリック透過スタイルと仮想デスクトップ追跡の関係（新発見）

**問題**: `WS_EX_TRANSPARENT` + `WS_EX_NOACTIVATE` がウィンドウ**生成時**（OnSourceInitialized）に付いていると、OS が仮想デスクトップの追跡対象から外す。後から外しても手遅れ。

**発見経緯**:
- 編集ON（TRANSPARENT=False）で作成した付箋 → VD移動成功
- 編集OFF（TRANSPARENT=True）で作成した付箋 → VD移動失敗
- テスト 1B（スタイル除去後に Move）でも失敗 → OS が生成時に追跡可否を決定している

**対策**:
1. NoteWindow を**常に `clickThrough=false`**（クリック透過なし）で生成
2. `Show()` の**後に** `SetClickThrough(true)` / `SetInEditMode(true)` を適用
3. これにより OS に「通常ウィンドウ」として認識させてから透過を掛ける
4. Phase 8 の `MoveWindowToDesktop` は `Show()` と `SetClickThrough()` の間で行える

**副作用修正**: `clickThrough=false` にしたことで `IsInEditMode = !clickThrough = true` となり、編集OFF時でもUI要素が表示されるバグが発生 → `IsInEditMode` を常に `false` で初期化し、NoteManager が Show() 後に正しい状態を設定するよう修正。

---

## 2. Git コミット履歴（本セッション分）

```
c5dac61 docs: DJ-8知見を進捗ログに記録 — VD追跡とウィンドウスタイルの関係
ee2a329 fix: 編集OFF時の付箋作成でUI要素が表示される問題を修正
eebdbb6 fix(DJ-8): ウィンドウ生成時のクリック透過をShow()後に遅延適用
b3c1c30 docs: Phase 3.7（DJ-7）検証完了 — 全項目合格、実機確認済み
531e751 docs: Phase 3.7（DJ-7）実装完了 — TODO + 進捗ログ更新
0ef532e feat: DJ-7対応 — WS_EX_TOOLWINDOW をオーナーウィンドウ方式に変更
c4ac06a docs: Phase 3.7（DJ-7対応）をTODOに追加して作業開始
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
│   ├── App.xaml.cs                  ← ★ Phase 3.7: InitializeOwnerWindow() 呼び出し追加
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
│   │   ├── NoteManager.cs           ← ★ Phase 3.7: オーナーウィンドウ管理 + DJ-8遅延適用
│   │   └── VirtualDesktopService.cs ← Phase 3.5: COM + Registry + 短命Window
│   ├── Views/
│   │   ├── NoteWindow.xaml          ← Phase 3: WindowChrome + ツールバー + 下部アイコン
│   │   └── NoteWindow.xaml.cs       ← ★ Phase 3.7: TOOLWINDOW除去 + IsInEditMode=false固定
│   ├── ViewModels/                  ← （空、Phase 4〜 で使用）
│   └── Interop/
│       ├── NativeMethods.cs
│       └── VirtualDesktopInterop.cs ← Phase 3.5: IVirtualDesktopManager COM 定義
├── 引き継ぎ資料置き場/
│   ├── SESSION_HANDOVER_20260206_PART0〜3.md
│   ├── SESSION_HANDOVER_20260207_PART4.md
│   └── SESSION_HANDOVER_20260207_PART5.md ← 本ファイル
├── 要件定義.md
├── TODO.md
├── README.md
├── progress_log.txt
└── .gitignore
```

---

## 4. NoteWindow の生成フロー（DJ-7/DJ-8 反映後の確定版）

Phase 8 で MoveWindowToDesktop を実装する際の基盤となるフロー:

```
NoteManager.CreateNote() / DuplicateNote()
  │
  ├─ 1. new NoteWindow(model, clickThrough: false)
  │     └─ IsInEditMode = false（常に安全な初期状態）
  │     └─ OnSourceInitialized: TOOLWINDOW 除去のみ、TRANSPARENT/NOACTIVATE は付けない
  │
  ├─ 2. window.Owner = _ownerWindow（Alt+Tab 非表示 + VD管理参加）
  │
  ├─ 3. window.Show()
  │     └─ この時点で OS が「通常ウィンドウ」として追跡を開始する
  │
  ├─ 4. ★ Phase 8: ここで MoveWindowToDesktop(hwnd, desktopId) を呼ぶ
  │     └─ OS が追跡中なので移動が正常に成功する
  │
  └─ 5. if (IsEditMode) SetInEditMode(true) else SetClickThrough(true)
        └─ 最後に実際のモード状態を適用（TRANSPARENT/NOACTIVATE はここで初めて付く）
```

---

## 5. 次回やること: Phase 4（リッチテキスト編集 FR-TEXT）

### 5.1 タスク一覧（TODO.md Phase 4 セクション参照）

| タスク | 内容 |
|--------|------|
| P4-1 | NoteWindow に WPF RichTextBox 配置 |
| P4-2 | ツールバー実装（太字/下線/取り消し線/文字サイズ/文字色） |
| P4-3 | 適用ルール（選択範囲あり→選択に適用、なし→カーソル以後にトグル） |
| P4-4 | ツールチップ（機能名 + ショートカット表示） |
| P4-5 | Undo/Redo（Ctrl+Z / Ctrl+Y — RichTextBox 標準機能） |
| P4-6 | クリップボード（リッチ貼り付け優先 → プレーンフォールバック + フォント正規化） |

### 5.2 実装方針の提案

Phase 4 では NoteWindow.xaml の本文領域を大幅に変更する:

1. **現在の TextBlock → RichTextBox に置き換え**
   - `NoteWindow.xaml` の `NoteText`（TextBlock）を `RichTextBox` に変更
   - 編集ON + 選択中: IsReadOnly=false
   - 編集OFF: IsReadOnly=true + 背景透過

2. **ツールバーの拡張**
   - 現在の `ToolbarArea` 内に装飾ボタンを追加
   - ドラッグハンドル「⠿」は左端に残す

3. **RTF の保存/復元**（Phase 5 で本格化だが、メモリ上の RTF 操作は Phase 4 で必要）
   - `RichTextBox.Document` ↔ `FlowDocument` ↔ RTF 文字列変換

### 5.3 着手前に確認すべきこと

- 現在の `NoteWindow.xaml` の本文領域は `TextBlock`（Phase 4 で `RichTextBox` に置き換え）
- Phase 4 のコメントプレースホルダ:
  - `NoteWindow.xaml` L53: `<!-- Phase 4: ここにリッチテキスト装飾ツールバーを追加予定 -->`
  - `NoteWindow.xaml` L58-59: `<!-- Phase 4 で RichTextBox に置き換え予定 -->`

---

## 6. TODO 進捗状況

| Phase | 内容 | 状態 |
|-------|------|------|
| Phase 0 | プロジェクト基盤 | ✅ 完了 (2026-02-06) |
| Phase 1 | トレイ常駐 + 最小付箋 | ✅ 完了 (2026-02-06) |
| Phase 2 | Win32 Interop + モード切替 | ✅ 完了 (2026-02-06) |
| Phase 3 | 移動・リサイズ + 基本UI | ✅ 完了 (2026-02-07) |
| Phase 3.5 | 仮想デスクトップ技術スパイク | ✅ 完了 (2026-02-07) |
| Phase 3.7 | DJ-7/DJ-8: オーナーウィンドウ方式 | ✅ 完了 (2026-02-07) |
| **Phase 4** | **リッチテキスト編集** | **← 次回着手** |
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
- **DJ-8**: WS_EX_TRANSPARENT/NOACTIVATE も生成時に付けるとVD追跡外 → Show()後に遅延適用

---

## 8. 既知の注意点

1. **H.NotifyIcon.Wpf のトレイメニューから MessageBox を出すと一瞬で消える**
   - `async + await Task.Delay(300)` でメニュー閉じ待ちが必要
2. **PowerShell で日本語コミットメッセージ**: `-F ファイル` 方式を使う（`&&` はPowerShell旧バージョンで使えない）
3. **実行中プロセスのロック**: ビルド前に `Stop-Process -Name "TopFusen" -Force` で停止
4. **クリック透過は三重制御**（DJ-6）: WM_NCHITTEST フックとの共存に注意
5. **WindowChrome + AllowsTransparency=True**: .NET 8 WPF で正常動作確認済み。UseAeroPeek プロパティは存在しない
6. **DJ-8: ウィンドウ生成順序が超重要**: Show() → MoveWindowToDesktop → SetClickThrough の順序を守ること
7. **WPF Owner 注意**: Owner が先に Close されると子ウィンドウも連鎖で閉じる。CloseAllWindows では Owner 解除→子Close→オーナーClose の順序で行う

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
> 3. `引き継ぎ資料置き場/SESSION_HANDOVER_20260207_PART5.md` — 本ファイル
>
> **読み終わったら:**
> 1. **Phase 4（リッチテキスト編集）のタスクを確認**
> 2. **実装方針を3案提示**（PRD FR-TEXT と TODO.md の P4-1〜P4-6 を基に）
> 3. **ユーザーが案を選択後、TODO.md を更新して実装開始**
