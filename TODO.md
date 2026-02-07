# TopFusen v0.2.0 — TODO（全体実装計画）

> 方針: 案C（積極的・機能フル実装路線）+ 案B テスト戦略（各Phase検証 → 最終統合テスト）
> アーキテクチャ: **A案（付箋＝複数Window）** 採用（仮想デスクトップ MoveWindowToDesktop が HWND 単位のため）
> 作成日: 2026-02-06
> PRD: v0.2.0（`要件定義.md` に全文記載）

---

## 仮決め事項（PRD未確定値）

| 項目 | 仮値 | 根拠 |
|------|------|------|
| Company名 | `TopFusen` | シンプルに |
| App名 | `TopFusen` | 同上 |
| 最小サイズ | 160×120 px | PRD例示値を採用 |
| デバウンス秒数 | 3秒 | 一般的なバランス |
| ログ保持 | 7日分 / 最大7ファイル | 十分かつ肥大化しない |
| 新規付箋の初期サイズ | 240×180 px | 実用的な最小〜中サイズ |
| 新規付箋の初期配置 | 現在デスクトップのプライマリモニタ中央付近（重なり検知+ずらし） | FR仕様6.3準拠 |
| パレット色 | 実装時にビビッド8色/ナチュラル8色を仮定義 | 後で差し替え可能なデータ駆動 |
| PerMonitorV2 | 必須（manifest明示宣言） | DPI混在環境でのズレ回避 |

---

## 設計判断ログ（レビュー反映）

> 以下はレビュー指摘を受けて追加した設計判断。背景と理由を記録する。

### DJ-1: A案（複数Window）を維持する理由
- B案（ホスト1枚Window内にControl配置）は Z順を WPF ZIndex で完全制御できる利点がある
- しかし仮想デスクトップの MoveWindowToDesktop は HWND 単位のため、B案だとデスクトップごとにホストが必要になり複雑化する
- マルチモニタでも各ウィンドウが自然にモニタに配置される A案が有利
- **A案の Z順問題は「Activated イベントでの再適用」で解決可能**

### DJ-2: Z順ポリシー — クリックで前面化しない（固定方式）
- PRD FR-ZORDER: 「設定画面の D&D のみで調整」
- 編集モードで付箋をクリック/アクティブ化しても、設定した Z順を維持する
- 実装: Activated / GotFocus イベントで Z順を再適用（SetWindowPos し直す）

### DJ-3: 座標保存の単位ルール
- WPF の Window.Left / Top / Width / Height は DIP（device-independent pixels）
- 保存時: **Relative（0〜1、WorkArea基準）を主** とし、DIP値 + DpiScale を補助として保持
- 復元時: 同一モニタ → Relative 優先 → WorkArea に再投影 → クランプ
- DIP → 物理px 変換が必要な場合: `物理px = DIP × (DpiScale)`
- DPI 取得: `VisualTreeHelper.GetDpi()` または `PresentationSource` 経由

### DJ-4: 仮想デスクトップ COM 呼び出しスレッド方針
- IVirtualDesktopManager の COM 呼び出しは **UI スレッドから行う**（STA COM のため）
- ただし重い処理（Registry 読み等）はバックグラウンドで行い、結果を UI スレッドに戻す
- COM 初期化失敗時はログ出力 + 仮想デスクトップ機能を graceful に無効化

### DJ-7: WS_EX_TOOLWINDOW は仮想デスクトップ管理の対象外（Phase 3.5 スパイクで判明）
- **問題**: `WS_EX_TOOLWINDOW` を持つオーナーなしウィンドウは、OS の仮想デスクトップ管理に参加しない
  - `GetWindowDesktopId` が `Guid.Empty` を返す（OS が追跡していない）
  - `MoveWindowToDesktop` は成功を返すが実際には移動しない
  - 結果としてウィンドウが全デスクトップで表示される
- **検証**: 普通の Window（TOOLWINDOW なし）では `MoveWindowToDesktop` が正常に動作することを確認
- **対策（Phase 3.7 で実施済み）**: Alt+Tab 非表示を **オーナーウィンドウ方式** に変更
  - 非表示のオーナー Window を1つ作成し、NoteWindow の Owner に設定
  - オーナー付きウィンドウは Alt+Tab に出ない（WS_EX_TOOLWINDOW 不要）
  - かつ、仮想デスクトップ管理に正常に参加できる
  - `ShowInTaskbar=false` はそのまま維持

### DJ-8: WS_EX_TRANSPARENT/NOACTIVATE も生成時に付けると仮想デスクトップ追跡外（Phase 3.7 追加検証で判明）
- **問題**: `WS_EX_TRANSPARENT` + `WS_EX_NOACTIVATE` がウィンドウ生成時（OnSourceInitialized）に付いていると、OS が仮想デスクトップの追跡対象から外す
  - 後から外しても（テスト 1B）手遅れ — TOOLWINDOW と同じパターン
  - 編集ON（TRANSPARENT=False）で作成した付箋は移動成功、編集OFF（TRANSPARENT=True）で作成した付箋は移動失敗
- **対策（Phase 3.7 で実施済み）**: NoteWindow を**常にクリック透過なしで生成**し、`Show()` の**後に** `SetClickThrough()` を適用する
  - OS に「通常ウィンドウ」として認識させてから透過を掛ける
  - Phase 8 での MoveWindowToDesktop は Show() と SetClickThrough() の間で行う

### DJ-6: クリック透過は三重制御方式（Phase 2 で判明）→ ~~DJ-9 で WM_NCHITTEST 単独に変更~~
- ~~WPF `AllowsTransparency=True` は `WS_EX_LAYERED` を自動付与する~~
- ~~`WS_EX_LAYERED` 環境では `WS_EX_TRANSPARENT` の ON/OFF だけではクリック制御が不十分~~
- ~~三重制御方式を採用~~ → **DJ-9 で WM_NCHITTEST 単独制御に変更**

### DJ-9: クリック透過は WM_NCHITTEST 単独制御に変更（Phase 5 後に判明）
- **問題**: `WS_EX_TRANSPARENT` + `WS_EX_NOACTIVATE` を付けると（生成後であっても）OS の仮想デスクトップ追跡が破壊される
  - 編集OFF（スタイル付与）→ 全デスクトップで付箋が表示される
  - 編集ON（スタイル除去）→ 正常に1デスクトップに所属する
  - 再び編集OFF → また全デスクトップに表示される
- **DJ-8 のワークアラウンド（生成時クリーン → Show() 後適用）は不十分だった**
  - 生成時だけでなく、後から付けても OS は VD 追跡を止める
- **対策**: `WS_EX_TRANSPARENT` / `WS_EX_NOACTIVATE` を**一切使わない**
  - クリック透過は `WM_NCHITTEST` → `HTTRANSPARENT` のみで実現
  - `SetClickThrough()` は `_isClickThrough` フラグの切り替えのみ（Win32 スタイル操作なし）
  - OS がマウスメッセージ送信前に hit test を行い、HTTRANSPARENT なら背後ウィンドウに転送
  - ウィンドウスタイルを変更しないので VD 追跡が常に維持される
- **OnSourceInitialized** では念のため `WS_EX_TRANSPARENT` / `WS_EX_NOACTIVATE` / `WS_EX_TOOLWINDOW` を除去

### DJ-10: VD 自前管理方式の確定（案C — WS_EX_TRANSPARENT + DWMWA_CLOAK + ポーリング）
- **前提**: `WS_EX_TRANSPARENT` と OS の仮想デスクトップ追跡は共存不可能（DJ-8/DJ-9 で検証済み）
- **方針**: OS の VD 追跡に頼らず、アプリ側で自前管理する
- **クリック透過**: `WS_EX_TRANSPARENT` + `WS_EX_NOACTIVATE` + `WM_NCHITTEST` の三重制御を復活（DJ-6 に戻す）
  - ただし DJ-8 の「Show() 後に付与」は維持。DJ-9 は撤回
- **VD 切替検知**:
  1. レジストリ監視（`RegNotifyChangeKeyValue`）— 第一候補（Phase 8 本実装で導入予定）
  2. `DispatcherTimer` ポーリング（300ms）— Phase 8.0 スパイク + フォールバック
  - ※ 未公開 COM 通知は不採用（Windows Update で GUID 変更リスク大）
- **表示制御**:
  1. `DWMWA_CLOAK`（DWM Cloak）— 第一候補
  2. `ShowWindow(SW_HIDE)` — フォールバック
  - ※ `Opacity=0` は不採用（デバッグ困難＋副作用リスク）
- **VD Tracker Window**: `WS_EX_TRANSPARENT` なしの常駐 HWND で `IsWindowOnCurrentVirtualDesktop` を高速チェック
- **編集モード遷移シーケンス**（ちらつき対策の核心）:
  - 編集 OFF: ① 現在 VD 以外の付箋を Cloak → ② `WS_EX_TRANSPARENT` 付与
  - 編集 ON: ① `WS_EX_TRANSPARENT` 除去 → ② 該当 VD の付箋のみ Uncloak
- **デスクトップ喪失フォールバック**: 保存 `DesktopId` が現存しない場合 → 現在 VD に付替

---

## 引き継ぎルール

> **チャット枠が変わる際は、以下を必ず次の枠で参照すること:**
>
> 1. `要件定義.md` — PRD v0.2.0 全文（機能要件・技術仕様・テスト計画・参照リンク集）
> 2. `TODO.md` — 本ファイル（全体計画・進捗・仮決め事項・設計判断ログ）
> 3. 該当の `SESSION_HANDOVER_YYYYMMDD_PARTx.md` — 直近の引き継ぎ資料
> 4. `progress_log.txt` — 実装進捗ログ（存在する場合）

---

## Phase 0: プロジェクト基盤 ✅ (2026-02-06 完了)
> 目標: ビルド可能なソリューション + アーキテクチャ骨格 + 安全装置

- [x] P0-1: .NET 8 WPF ソリューション作成（TopFusen.sln）(2026-02-06 完了)
  - TopFusen（実行プロジェクト / WPF Application）
  - フォルダ構成: Models/, Views/, ViewModels/, Services/, Interop/, Assets/
- [x] P0-2: app.manifest 作成（PerMonitorV2 DPI宣言 + UAC=asInvoker）(2026-02-06 完了)
- [x] P0-3: App.xaml で ShutdownMode=OnExplicitShutdown 設定 (2026-02-06 完了)
- [x] P0-4: DI コンテナ導入（Microsoft.Extensions.DependencyInjection + Hosting）(2026-02-06 完了)
- [x] P0-5: ログ基盤構築（Serilog → ファイル出力、7日ローテーション）(2026-02-06 完了)
  - 出力先: %LocalAppData%\TopFusen\TopFusen\logs\app_yyyyMMdd.log
- [x] P0-6: 設定モデル定義（AppSettings / NoteModel）(2026-02-06 完了)
  - NoteId: GUID
  - Placement（RelativeX/Y, DipX/Y, DipWidth/DipHeight, DpiScale）※DJ-3 反映
  - MonitorIdentity（DevicePath, NameFallback）
  - Style（BgPaletteCategoryId, BgColorId, Opacity0to100, TextColor, FontFamilyName）
  - AppSettings（IsHidden, Hotkey, FontAllowList, ZOrderByDesktop）
- [x] P0-7: .gitignore 作成 (2026-02-06 完了)
- [x] P0-8: README.md 作成（プロジェクト概要 + ビルド手順）(2026-02-06 完了)
- [x] **P0-9: 単一インスタンス制御（Mutex + IPC）** (2026-02-06 完了)
  - 名前付き Mutex でプロセス重複検知
  - 2重起動時: 既存プロセスに NamedPipe で「設定を開く」コマンドを送り、新プロセスは終了
  - 根拠: 自動起動 + 手動起動の二重起動でトレイ重複・保存ファイル競合を防止
- [x] **P0-10: トレイ実装方式を確定 → H.NotifyIcon.Wpf 2.1.3** (2026-02-06 完了)
  - H.NotifyIcon.Wpf 2.1.3（net8.0-windows10.0.17763 互換確認済み）
- [x] **P0-VERIFY: Phase 0 検証** (2026-02-06 完了)
  - [x] ソリューションがビルドできる（エラー0、警告0）
  - [x] アプリが起動してトレイ常駐する（ShutdownMode=OnExplicitShutdown）
  - [x] ログファイルが所定パスに生成される（全7行の起動ログ確認済み）
  - [x] **2回起動しても2つ目が即終了し、IPCコマンド送受信成功**

---

## Phase 1: タスクトレイ常駐 + 最小付箋表示 ✅ (2026-02-06 完了)
> 目標: トレイに常駐し、付箋ウィンドウが1枚表示される（Alt+Tab/タスクバーに出ない）

- [x] P1-1: タスクトレイアイコン実装（P0-10 で確定した方式）(2026-02-06 完了)
  - アイコンリソース埋め込み（Assets/app.ico、PowerShell で生成）
  - H.NotifyIcon.Wpf 2.1.3 の TaskbarIcon + IconSource で表示
- [x] P1-2: トレイ右クリックメニュー骨格（FR-TRAY）(2026-02-06 完了)
  - 編集モード ON/OFF（トグル）— Phase 2 で本格実装
  - 新規付箋作成 — NoteManager.CreateNote() と連動
  - 一時的に非表示（トグル）— stub（Phase 10）
  - 設定を開く（stub）— Phase 11
  - 終了 — Application.Current.Shutdown() で完全終了
- [x] P1-3: NoteWindow 基本実装（Borderless WPF Window）(2026-02-06 完了)
  - WindowStyle=None, AllowsTransparency=True, Topmost=True
  - **ShowInTaskbar=False**
  - 最小サイズ 160×120
  - 背景色つき矩形として表示（#FFFBE38C）
- [x] P1-4: NoteManager 骨格（付箋の生成/保持/破棄）(2026-02-06 完了)
  - CreateNote() / DeleteNote() / CloseAllWindows()
  - プライマリモニタ中央に配置
- [x] **P1-5: NoteWindow を Alt+Tab / タスクバーから隠す** (2026-02-06 完了)
  - ShowInTaskbar = false（XAML）
  - WS_EX_TOOLWINDOW を付与（Win32 interop: Interop/NativeMethods.cs）
  - WS_EX_APPWINDOW を外す
- [x] **P1-VERIFY: Phase 1 検証** (2026-02-06 完了)
  - [x] トレイアイコンが表示される（ログ: 「トレイアイコンを初期化しました」）
  - [x] 右クリックメニューが出る
  - [x] 「新規付箋作成」で付箋ウィンドウが画面に出る
  - [x] 「終了」でプロセスが完全に終了する
  - [x] 付箋が Topmost で他ウィンドウより前面にいる
  - [x] Alt+Tab に付箋が出ない（WS_EX_TOOLWINDOW）
  - [x] タスクバーに付箋が出ない（ShowInTaskbar=False）

---

## Phase 2: Win32 Interop + モード切替 ✅ (2026-02-06 完了)
> 目標: 非干渉モード（クリック透過）と編集モードが切り替わる
> ★ ここが TopFusen の最重要技術検証ポイント
> ★ 透明Window + クリック透過 + TopMost の共存を検証する

- [x] P2-1: Win32 Interop ヘルパー拡張（Interop/NativeMethods.cs）(2026-02-06 完了)
  - WS_EX_TRANSPARENT / WS_EX_LAYERED / WS_EX_NOACTIVATE 定数追加
- [x] P2-2: NoteWindow にクリック透過の ON/OFF 実装 (2026-02-06 完了)
  - 三重制御方式: WS_EX_TRANSPARENT + WS_EX_NOACTIVATE + WM_NCHITTEST フック
  - ★重要知見: WPF AllowsTransparency=True（WS_EX_LAYERED）環境では
    WS_EX_TRANSPARENT の ON/OFF だけではクリック制御が不十分。
    WM_NCHITTEST で HTTRANSPARENT を返す方式を併用する必要がある
- [x] P2-3: AppHost にモード管理（IsEditMode プロパティ）(2026-02-06 完了)
  - NoteManager.SetEditMode() で全付箋に一括適用
  - トレイメニューのトグルと連動
  - 新規作成時も現在のモードを自動適用
- [x] **P2-VERIFY: Phase 2 検証（★最重要）** (2026-02-06 完了)
  - [x] 非干渉モード: 付箋上をクリックすると背後アプリが反応する
  - [x] 非干渉モード: 付箋がフォーカスを奪わない
  - [x] 編集モード: 付箋をクリックで選択・操作できる
  - [x] 編集モード: 付箋以外をクリックしても編集OFFに戻らない
  - [x] トレイメニューで ON/OFF が正しくトグルする
  - [x] TopMost が維持されている（他ウィンドウで隠れない）
  - [x] **AllowsTransparency=True の状態で上記すべてが成立する**
  - [x] **Alt+Tab / タスクバーに付箋が出ない状態が維持されている**

---

## Phase 3: 付箋の移動・リサイズ + 基本UI ✅ (2026-02-07 完了)
> 目標: 編集モードで付箋をドラッグ移動・リサイズできる
> 実装方針: **案B（WindowChrome + DragMove + 部分バインディング）**

- [x] P3-1: WindowChrome 導入（リサイズハンドル有効化）(2026-02-07 完了)
  - ResizeBorderThickness=6, CaptionHeight=0, GlassFrameThickness=0
  - ★ UseAeroPeek は .NET 8 の WindowChrome に存在しないため除外
- [x] P3-2: ドラッグ移動実装（ツールバー領域で DragMove）(2026-02-07 完了)
- [x] P3-3: リサイズ実装（角/辺ドラッグ）+ MinWidth/MinHeight 制約 (2026-02-07 完了)
- [x] P3-4: 編集ON時の選択状態管理 (2026-02-07 完了)
  - 付箋クリック（OnActivated）→ NoteManager.SelectNote で「選択中」
  - 選択中の付箋のみ: ツールバー + 下部アイコン + 枠/影 表示
  - 未選択: 本文のみ（UpdateVisualState で一括制御）
- [x] P3-5: 付箋の削除ボタン（下部ゴミ箱アイコン）(2026-02-07 完了)
  - **削除時に RTF ファイル（notes/{NoteId}.rtf）も削除する** ← Phase 5 で実装
- [x] P3-6: 付箋の複製ボタン（下部コピーアイコン）(2026-02-07 完了)
  - 複製時 +24px,+24px ずらし + スタイルコピー + WorkArea クランプ
- [x] P3-7: 編集OFF時にすべてのUI要素（ツールバー/アイコン/枠）を非表示 (2026-02-07 完了)
- [x] **P3-VERIFY: Phase 3 検証** (2026-02-07 完了)
  - [x] ドラッグで付箋を移動できる
  - [x] 角/辺ドラッグでリサイズできる
  - [x] 最小サイズ（160×120）以下にならない
  - [x] 選択中の付箋だけツールバー/アイコン/枠が表示される
  - [x] 未選択付箋は本文のみ
  - [x] 削除ボタンで付箋が消える
  - [ ] **削除した付箋がディスク上からも消えている（RTFファイル）** ← Phase 5 で検証
  - [x] 複製ボタンで +24px ずれた付箋が生成される
  - [x] 編集OFF時にすべてのUI要素が消える
  - [x] **AllowsTransparency + WindowChrome でリサイズが正常に動作する**

---

## Phase 3.5: 仮想デスクトップ 技術スパイク ✅ (2026-02-07 完了)
> 目標: 仮想デスクトップ API の成立を早期検証する（Phase 8 の前倒しリスク軽減）
> ※ 本格実装は Phase 8。ここでは「使えるか」「どう使うか」を検証するだけ
> 実装方針: **案B（全API検証 + VirtualDesktopService分離 + トレイ検証メニュー）**

- [x] P3.5-1: COM Interop — IVirtualDesktopManager の初期化成立確認 (2026-02-07 完了)
  - CLSID_VirtualDesktopManager の CoCreateInstance → ✅ 成功
  - 失敗時の graceful 無効化パス → ✅ try/catch で IsAvailable=false
- [x] P3.5-2: 現在デスクトップID取得（短命ウィンドウ方式）の実証 (2026-02-07 完了)
  - 短命 Window を作成 → GetWindowDesktopId → GUID 取得 → Window 破棄 → ✅ 成功
- [x] P3.5-3: MoveWindowToDesktop で NoteWindow を別デスクトップへ移動できることを確認 (2026-02-07 完了)
  - ⚠️ **普通の Window は移動成功。NoteWindow は WS_EX_TOOLWINDOW が原因で移動不可**
  - 原因: WS_EX_TOOLWINDOW（オーナーなし）は仮想デスクトップ管理の対象外（DJ-7）
  - 対策: Phase 8 でオーナーウィンドウ方式に変更する
- [x] P3.5-4: Registry からデスクトップ一覧を読む実験（ベストエフォート）(2026-02-07 完了)
  - VirtualDesktopIDs byte[] → GUID[] パース → ✅ 成功
  - デスクトップ名取得 → ✅ 成功
- [x] **P3.5-VERIFY: スパイク検証** (2026-02-07 完了)
  - [x] COM 初期化が成功する
  - [x] 現在デスクトップ ID が GUID として取得できる
  - [x] NoteWindow を Desktop B へ移動 → ⚠️ 現状不可（DJ-7: TOOLWINDOW 問題）。Phase 8 でオーナーウィンドウ方式に変更予定
  - [x] COM 失敗時にアプリがクラッシュしない（graceful 無効化）
  - [x] **スパイク結果を progress_log.txt に記録**

---

## 🔖 引き継ぎポイント① — 基本動作 + 技術検証完成
> Phase 0〜3.5 完了時点で SESSION_HANDOVER を作成
> ここまでで「トレイ常駐 + 付箋表示 + クリック透過 + 移動リサイズ + 仮想デスクトップ技術検証」が検証済み
>
> **次の枠への指示:**
> 「`要件定義.md`（PRD v0.2.0）、`TODO.md`、本引き継ぎ資料を読んでから作業を開始して」

- [ ] HANDOVER-1: SESSION_HANDOVER_20260206_PART1.md 作成
  - 実施内容（詳細）
  - Git コミット履歴
  - TODO進捗状況
  - 各Phase検証結果（P0〜P3.5-VERIFY の結果）
  - **仮想デスクトップスパイクの結果・制約・判明した注意点** ← NEW
  - 次回対応すべきこと（Phase 4〜 の具体的な着手方針）
  - 現状コードの構成と該当箇所（引用付き）
  - 既知の問題・注意点

---

## Phase 3.7: DJ-7 対応 — WS_EX_TOOLWINDOW → オーナーウィンドウ方式 ✅ (2026-02-07 完了)
> 目標: Alt+Tab 非表示を WS_EX_TOOLWINDOW ではなくオーナーウィンドウ方式に変更し、仮想デスクトップ管理に参加できるようにする
> 背景: Phase 3.5 スパイクで判明した DJ-7 問題（WS_EX_TOOLWINDOW ウィンドウは MoveWindowToDesktop が効かない）
> ★ Phase 4〜7 で NoteWindow を大量改修する前に基盤変更を確定させる（手戻りリスク回避）

- [x] P3.7-1: NoteManager にオーナーウィンドウ生成・管理を追加 (2026-02-07 完了)
  - 非表示の Window を1つ作成（Width=0, Height=0, Visibility=Hidden）
  - `WindowInteropHelper.EnsureHandle()` で HWND を確保
  - NoteWindow 作成時に `Owner` として設定
  - アプリ終了時にオーナーウィンドウを確実に Close
- [x] P3.7-2: NoteWindow から WS_EX_TOOLWINDOW を除去 (2026-02-07 完了)
  - `OnSourceInitialized` の `WS_EX_TOOLWINDOW` 付与 + `WS_EX_APPWINDOW` 除去を削除
  - Owner 付きウィンドウ + ShowInTaskbar=false で Alt+Tab 非表示が成立
  - クリック透過（三重制御: WS_EX_TRANSPARENT + WS_EX_NOACTIVATE + WM_NCHITTEST）はそのまま維持
- [x] **P3.7-VERIFY: DJ-7 検証** (2026-02-07 完了 ★実機確認済み)
  - [x] Alt+Tab に付箋が出ない
  - [x] タスクバーに付箋が出ない
  - [x] `GetWindowDesktopId` が有効な GUID を返す（Guid.Empty ではない）
  - [x] `MoveWindowToDesktop` で実際に付箋が別デスクトップへ移動する（ExStyle=0x00080108, TOOLWINDOW=False で成功）
  - [x] クリック透過（三重制御）が引き続き正常に動作する
  - [x] Topmost が維持されている（TOPMOST=True 確認済み）
  - [x] AllowsTransparency + Owner の組み合わせで問題がない
  - [x] ドラッグ移動・リサイズが引き続き正常に動作する
  - [x] 複数付箋の選択状態管理が引き続き正常

---

## Phase 4: リッチテキスト編集（FR-TEXT） ✅ (2026-02-07 完了)
> 目標: 付箋内で文字単位の装飾ができる
> 実装方針: **案B（バランス）** — P4-1〜P4-6 全タスクを一括実装
> WPF EditingCommands 活用 + TextRange.ApplyPropertyValue() で装飾

- [x] P4-1: NoteWindow に WPF RichTextBox 配置 (2026-02-07 完了)
  - TextBlock → RichTextBox に置き換え
  - 編集ON + 選択中: IsReadOnly=false, Focusable=true（編集可能）
  - 編集ON + 未選択: IsReadOnly=true, IsHitTestVisible=true（クリックで選択可能）
  - 編集OFF: Focusable=false, IsHitTestVisible=false（非干渉モード）
  - スクロールバー: 編集時Auto / 非編集時Hidden
  - フォーカス管理: Keyboard.ClearFocus() で安全にクリア
- [x] P4-2: ツールバー実装（上部配置）(2026-02-07 完了)
  - ドラッグハンドル「⠿」（左端、DockPanel.Dock=Left）
  - 太字 `B`（Ctrl+B — EditingCommands.ToggleBold）
  - 下線 `U`（Ctrl+U — EditingCommands.ToggleUnderline）
  - 取り消し線 `S`（TextRange.ApplyPropertyValue で TextDecorations 手動トグル）
  - 文字サイズ ComboBox（8, 10, 12, 14, 16, 18, 20, 24, 28, 36, 48）
  - 文字色パレット Popup（黒/白/赤/青/緑/オレンジ/紫/ピンク/水色/黄色 の10色）
  - SelectionChanged でツールバーボタン状態を自動同期
  - ボタンは Focusable=False（RichTextBox のフォーカスを奪わない）
- [x] P4-3: 適用ルール実装（FR-TEXT-4）(2026-02-07 完了)
  - 選択範囲あり → 選択範囲に適用（TextRange.ApplyPropertyValue）
  - 選択範囲なし → カーソル以後のトグル状態保持（WPF springloaded formatting）
- [x] P4-4: ツールチップ実装（機能名 + ショートカット表示）(2026-02-07 完了)
  - 太字: "太字 (Ctrl+B)", 下線: "下線 (Ctrl+U)", 取り消し線: "取り消し線"
  - 文字サイズ: "文字サイズ", 文字色: "文字色"
  - カラーパレット各色にも色名 ToolTip 付き
- [x] P4-5: Undo/Redo（Ctrl+Z / Ctrl+Y）(2026-02-07 完了)
  - WPF RichTextBox 標準機能で対応済み（追加コード不要）
- [x] P4-6: クリップボード対応（FR-TEXT-6）(2026-02-07 完了)
  - リッチ貼り付け優先 → プレーンフォールバック（WPF 標準動作）
  - 貼り付け後にドキュメント全体のフォントを付箋フォントに正規化
  - DataObject.Pasting + Dispatcher.BeginInvoke で安全にポスト処理
- [x] **P4-VERIFY: Phase 4 検証（ビルド検証済み — 実機検証は下記チェックリスト）** (2026-02-07 完了)
  - [ ] 文字入力ができる ← 実機確認待ち
  - [ ] 太字/下線/取り消し線が文字単位で適用される ← 実機確認待ち
  - [ ] 文字サイズ変更が文字単位で反映される ← 実機確認待ち
  - [ ] 文字色変更が反映される ← 実機確認待ち
  - [ ] 選択範囲なしの場合、カーソル以後に適用される ← 実機確認待ち
  - [ ] ツールチップに機能名+ショートカットが出る ← 実機確認待ち
  - [ ] Ctrl+Z / Ctrl+Y が効く ← 実機確認待ち
  - [ ] Ctrl+C / Ctrl+V が効く（リッチ→フォールバック）← 実機確認待ち
  - [ ] 貼り付け時にフォントが付箋フォントに正規化される ← 実機確認待ち

---

## Phase 5: 永続化（FR-PERSIST） ✅ (2026-02-07 完了)
> 目標: 再起動後に付箋が完全復元される
> 実装方針: **案B（PRD仕様準拠）** — Atomic Write + デバウンス + 破損検知フォールバック

- [x] P5-1: Persistence サービス実装 (2026-02-07 完了)
  - 保存先: %LocalAppData%\TopFusen\TopFusen\
  - PersistenceService.cs: JSON/RTF ファイル I/O + Atomic Write + デバウンス + 破損検知
- [x] P5-2: notes.json 保存/読込（全 Note メタデータ）(2026-02-07 完了)
- [x] P5-3: notes/{NoteId}.rtf 保存/読込（RTF本文）(2026-02-07 完了)
  - NoteWindow.GetRtfBytes() / LoadRtfBytes() で TextRange.Save/Load
- [x] P5-4: settings.json 保存/読込（AppSettings）(2026-02-07 完了)
- [x] P5-5: Atomic Write 実装（tmp → File.Replace → .bak 生成）(2026-02-07 完了)
- [x] P5-6: 破損検知 + .bak フォールバック + ユーザー通知 (2026-02-07 完了)
  - LoadJsonWithFallback<T> で自動フォールバック + MessageBox 通知
- [x] P5-7: デバウンス保存（3秒）— テキスト/移動/リサイズ変更時 (2026-02-07 完了)
  - DispatcherTimer（UIスレッド） + NoteChanged イベント連動
- [x] P5-8: 終了時の強制フラッシュ（FR-TRAY-5 連携）(2026-02-07 完了)
- [x] **P5-9: SessionEnding / Application.Exit フック** (2026-02-07 完了)
  - Windows ログオフ / シャットダウン時にも確実に同期保存
  - OnExit: FlushSave() → CloseAllWindows()（ウィンドウ閉じる前に保存）
- [x] P5-10: 起動時ロード → NoteManager.LoadAll() で復元 (2026-02-07 完了)
  - 起動直後は必ず編集OFF（FR-BOOT-2）
  - RestoreNote(): NoteWindow 作成 → RTF読込 → 編集OFF → 変更追跡有効化
- [x] **P5-11: 削除時のファイル掃除** (2026-02-07 完了)
  - DeleteNote 時: RTFファイル削除 + ScheduleSave()
  - 起動時: CleanupOrphanedRtfFiles() で孤立RTF自動削除
- [ ] **P5-VERIFY: Phase 5 検証**
  - [ ] 付箋を作成して内容を書く → アプリ終了 → 再起動で内容が復元される
  - [ ] 位置/サイズを変更 → 再起動で復元される
  - [ ] 起動直後が必ず編集OFF
  - [ ] %LocalAppData% に JSON/RTF ファイルが生成されている
  - [ ] .bak ファイルが存在する
  - [ ] settings.json を壊す → 起動時に .bak から復旧し通知が出る
  - [ ] デバウンス: 連続変更が1回の保存にまとまる（ログで確認）
  - [ ] **付箋削除 → 再起動 → 復元されない + RTFファイルも消えている**
  - [ ] **孤立RTFファイルを手動配置 → 起動時に掃除される**

---

## 🔖 引き継ぎポイント② — 永続化完成
> Phase 4〜5 完了時点で SESSION_HANDOVER を作成
> ここまでで「リッチテキスト付箋が再起動後も復元される」検証済み状態
>
> **次の枠への指示:**
> 「`要件定義.md`（PRD v0.2.0）、`TODO.md`、本引き継ぎ資料を読んでから作業を開始して」

- [ ] HANDOVER-2: SESSION_HANDOVER_YYYYMMDD_PART2.md 作成
  - 実施内容（詳細）
  - Git コミット履歴
  - TODO進捗状況
  - 各Phase検証結果（P4〜P5-VERIFY の結果）
  - 次回対応すべきこと
  - 現状コードの構成と該当箇所（引用付き）
  - 既知の問題・注意点

---

## Phase 8.0: VD 自前管理 技術スパイク ✅ (2026-02-07 完了)
> 目標: DJ-10 方式（WS_EX_TRANSPARENT + DWMWA_CLOAK + ポーリング）の技術検証
> ※ Phase 8 本格実装の前に「動くか」を確認するスパイク
> 背景: WS_EX_TRANSPARENT と OS の VD 追跡が共存不可能であることが判明（DJ-8/DJ-9）
> 方針: OS の VD 追跡に頼らず、DWMWA_CLOAK で自前管理する

- [x] P8.0-1: NativeMethods 拡張（DwmSetWindowAttribute / DWMWA_CLOAK / SetWindowPos）(2026-02-07 完了)
- [x] P8.0-2: VD Tracker Window 実装（WS_EX_TRANSPARENT なし常駐 HWND）(2026-02-07 完了)
- [x] P8.0-3: DWMWA_CLOAK による Cloak/Uncloak 実装 (2026-02-07 完了)
- [x] P8.0-4: DispatcherTimer ポーリングによる VD 切替検知 (2026-02-07 完了)
- [x] P8.0-5: WS_EX_TRANSPARENT 復活（DJ-9 撤回 → DJ-6 三重制御に戻す）(2026-02-07 完了)
- [x] P8.0-6: 基本的な VD 表示制御（デスクトップ切替時に Cloak/Uncloak）(2026-02-07 完了)
- [x] P8.0-7: 付箋作成時に現在 DesktopId を付与 + 起動復元時の VD 振り分け (2026-02-07 完了)
- [x] P8.0-8: トレイデバッグメニューに検証項目追加 (2026-02-07 完了)
- [x] **P8.0-VERIFY: スパイク検証** (2026-02-07 完了 ★実機確認済み)
  - [x] DWMWA_CLOAK で NoteWindow が隠れる / 再表示される
  - [x] VD Tracker Window で現在のデスクトップ ID が取得できる
  - [x] クリック透過が復活している（編集 OFF 時にクロスプロセスで透過）
  - [x] デスクトップ切替時に正しい付箋だけが表示される
  - [x] Uncloak 後に Topmost が維持されている
  - [x] 再起動後に DesktopId が復元され、正しい VD に付箋が表示される
  - [x] **追加検証**: 編集ON中のVD切替でも正しく分離される（Fix2 適用後）
  - [x] **追加検証**: 起動順序修正後、起動直後から正しいVDに付箋が配置される（Fix1 適用後）

---

## Phase 6: 見た目・スタイル（FR-STYLE / FR-FONT） ✅ (2026-02-07 完了)
> 目標: 背景色パレット + 不透明度 + フォント選択が動く
> 実装方針: **案B（バランス）** — P6-1〜P6-8 全タスク一括実装
> 不透明度: 背景色の Alpha チャネルで制御（テキスト・枠・影は常に不透明 → FR-STYLE-3 自然対応）

- [x] P6-1: カラーパレット定義（データ駆動）(2026-02-07 完了)
  - ビビッド系 8色 + ナチュラル系 8色
  - Models/Palette.cs（PaletteDefinitions 静的クラス + PaletteCategory/PaletteColor record）
- [x] P6-2: 背景色選択UI（カテゴリ → 色選択）(2026-02-07 完了)
  - 下部バーにスタイルボタン（🎨 カラーインジケータ付き）
  - Popup: カテゴリラジオ（ビビッド/ナチュラル）+ 色グリッド（UniformGrid 4×2）
  - 選択ハイライト（太枠）+ 即時反映
- [x] P6-3: 不透明度スライダー（0〜100、背景Alpha連動）(2026-02-07 完了)
  - Popup 内 Slider（0〜100）+ パーセント表示
  - 背景色の Alpha = Opacity/100 × 255（テキストは不透明維持）
- [x] P6-4: 編集ON時の操作視認性（FR-STYLE-3）(2026-02-07 完了)
  - 背景 Alpha 方式により、枠線/影は常に不透明で表示される
  - 不透明度 0 でも枠/影でドラッグ・リサイズ可能
- [x] P6-5: 文字色選択（黒/白 + パレット）(Phase 4 で実装済み)
  - 10色パレット Popup（黒/白/赤/青/緑/オレンジ/紫/ピンク/水色/黄色）
- [x] P6-6: フォント選択UI (2026-02-07 完了)
  - Popup 内 ComboBox（FontAllowList から取得）
  - 選択 → ドキュメント全体に適用（ApplyFontToDocument）
  - 現在フォントが許可リストにない場合は一時追加
- [x] P6-7: フォント許可リスト管理（ロジック）(2026-02-07 完了)
  - AppSettings.FontAllowList（9フォントプリセット）
  - NoteManager → NoteWindow.SetFontAllowList() で配信
  - 管理UI は Phase 11（設定画面）で実装
- [x] P6-8: 貼り付け時のフォント正規化（付箋フォントに統一）(Phase 4 で実装済み)
  - NormalizePastedFont() — DataObject.Pasting + Dispatcher.BeginInvoke
- [ ] **P6-VERIFY: Phase 6 検証**
  - [ ] パレットから背景色を選択すると即時反映
  - [ ] カテゴリ切替（ビビッド/ナチュラル）で色グリッドが変わる
  - [ ] 不透明度スライダーが 0〜100 で動作（0=完全透明、100=不透明）
  - [ ] 不透明度 0〜10 でも編集ON時に枠/影で操作可能
  - [ ] 文字色（黒/白/パレット）が反映される
  - [ ] フォント選択が付箋全体に適用される
  - [ ] 許可リスト外のフォントは選択UIに出ない
  - [ ] 背景色/不透明度/フォントが再起動後も復元される
  - [ ] 複製時にスタイルがコピーされる
  - [ ] スタイルカラーインジケータが現在の背景色を表示する

---

## Phase 7: マルチモニタ対応（FR-MON）
> 目標: 複数モニタに配置して正しく復元される（DPI混在環境を含む）

- [ ] P7-1: Interop — EnumDisplayMonitors / GetMonitorInfo ラッパー
- [ ] P7-2: Interop — DisplayConfig monitorDevicePath 取得
- [ ] P7-3: 保存時: 所属モニタ識別子 + 相対座標 + DIP座標 + DpiScale を保持
- [ ] P7-4: 復元時: モニタ照合ロジック
  - monitorDevicePath 一致 → そこへ復元
  - 不一致 → szDevice フォールバック
  - 不明 → プライマリモニタ
- [ ] P7-5: 画面内クランプ処理（work area 内に100%収まる補正）
- [ ] P7-6: 新規作成時の重なり検知 + ずらし（+24px、最大10回）
- [ ] **P7-7: DPI/座標 変換ルールの実装** ← NEW（DJ-3 反映）
  - 保存: Relative（0〜1、WorkArea基準）を主。DIP + DpiScale を補助
  - 復元: 同一モニタ → Relative → WorkArea 再投影 → クランプ
  - DPI 取得: VisualTreeHelper.GetDpi() / PresentationSource 経由
  - 異なる DPI モニタ間の移動時、座標変換を正しく行う
- [ ] **P7-VERIFY: Phase 7 検証**
  - [ ] 付箋をモニタAに配置 → 再起動後もモニタAに出る
  - [ ] （マルチモニタ環境で）別モニタへ移動 → 保存 → 再起動 → 正しい位置
  - [ ] モニタを外した状態で起動 → プライマリに収まる
  - [ ] 画面外にはみ出す座標 → クランプされて必ず見える位置
  - [ ] 新規作成が既存付箋と重なりそうな場合 → ずれて配置される
  - [ ] **100%/150% 混在の2モニタで、移動→再起動→同位置復元** ← NEW
  - [ ] **スケール変更後に再起動→クランプされて破綻しない** ← NEW

---

## Phase 8: 仮想デスクトップ対応（FR-VDSK）✅ (2026-02-07 完了)
> 目標: デスクトップ単位で付箋が分離・復元される
> ※ Phase 3.5 → Phase 8.0 で DJ-10 方式の検証済み → ここで本格実装
> ★ Phase 6/7 より前倒しで実装する（クリック透過と VD 追跡の矛盾を早期解決）

- [x] P8-1: スパイクコードを正式実装へ昇格 (Phase 8.0 で実装済み)
  - IVirtualDesktopManager ラッパー（VirtualDesktopService.cs）
  - COM 初期化失敗時の graceful 無効化
  - DJ-7: オーナーウィンドウ方式（Phase 3.7 で実装済み）
  - DJ-10: DWMWA_CLOAK + VD Tracker Window + ポーリング
- [x] P8-2: 現在デスクトップID取得の安定実装 (Phase 8.0 で実装済み)
  - 3段階取得: Tracker HWND → Registry → 短命ウィンドウ
- [x] P8-3: Registry デスクトップ一覧取得（ベストエフォート）(Phase 8.0 で実装済み)
  - VirtualDesktopIDs byte[] → GUID[] パース + 名前取得
  - 取得失敗 → 現在デスクトップのみ扱い
- [x] P8-4: 付箋作成時に現在デスクトップIDを付与 (Phase 8.0 で実装済み)
- [x] P8-5: 復元時の VD 振り分け (Phase 8.0 で DJ-10 方式に変更)
  - MoveWindowToDesktop → DWMWA_CLOAK 自前管理に変更
  - RestoreNote 内で非現在VD付箋を Cloak
- [x] P8-6: デスクトップ喪失フォールバック (2026-02-07 完了)
  - 起動時: 保存 DesktopId を Registry 一覧と照合 → 存在しなければ現在VDに付替
  - VirtualDesktopService.IsDesktopAlive() / FindOrphanedDesktopIds() を追加
  - NoteManager.RescueOrphanedNotes() で一括救済 + 保存スケジュール
  - VD が1つだけの場合: Registry 一覧が空でも現在VDと比較して判定
- [x] P8-7: ポーリング間隔最適化 (2026-02-07 完了)
  - 300ms → 500ms に変更（CPU 負荷軽減、体感遅延は十分許容範囲）
  - 将来的に RegNotifyChangeKeyValue への切替を検討（Phase 8 では見送り）
- [x] P8-8: スパイクコード整理 (2026-02-07 完了)
  - コメント・ログの「Phase 8.0」→「Phase 8」統一
  - デバッグメニューをスパイク検証用から正式デバッグ機能に格上げ（ラベル簡潔化）
- [x] **P8-VERIFY: Phase 8 検証** (2026-02-07 完了 ★実機確認済み)
  - [x] Desktop A で作成した付箋が Desktop B で見えない
  - [x] Desktop B で作成した付箋が Desktop A で見えない
  - [x] 再起動後も所属デスクトップに復元される
  - [x] **デスクトップ削除後に起動 → フォールバックで見失わない** ← P8-6 OK
  - [x] **VD 1つだけの環境でも正常動作** ← P8-6 OK
  - [x] 編集ON中の VD 切替でも正しく分離される
  - [x] クリック透過（クロスプロセス）が復活している
  - [x] Uncloak 後 Topmost が維持されている
  - [x] VD 切替の体感遅延なし（500ms ポーリング）
  - ⚠️ **後回し**: VD 削除時のリアルタイム救済が動作しない（再起動で復旧可能）→ 後回し.md 参照

---

## 🔖 引き継ぎポイント③ — マルチ環境対応完成
> Phase 6〜8 完了時点で SESSION_HANDOVER を作成
> ここまでで「スタイル + マルチモニタ + 仮想デスクトップ」が検証済み状態
>
> **次の枠への指示:**
> 「`要件定義.md`（PRD v0.2.0）、`TODO.md`、本引き継ぎ資料を読んでから作業を開始して」

- [ ] HANDOVER-3: SESSION_HANDOVER_YYYYMMDD_PART3.md 作成
  - 実施内容（詳細）
  - Git コミット履歴
  - TODO進捗状況
  - 各Phase検証結果（P6〜P8-VERIFY の結果）
  - 次回対応すべきこと
  - 現状コードの構成と該当箇所（引用付き）
  - 既知の問題・注意点

---

## Phase 9: Z順管理（FR-ZORDER）✅ (2026-02-07 完了)
> 目標: 付箋の前後関係をD&Dで変更でき、クリックで崩れない
> 実装方針: **案C（積極的）** — GongSolutions.Wpf.DragDrop 4.0.0 による本格D&D + Z順ロジック一括実装

- [x] **P9-0: Z順固定ポリシーの実装**（DJ-2 反映）(2026-02-07 完了)
  - ポリシー: クリック/アクティブ化で Z順を変えない（設定D&Dのみ）
  - NoteManager.OnNoteActivated → SelectNote + ApplyZOrder で Z順を即座に再適用
  - SetEditMode / HandleDesktopSwitch 後にも ApplyZOrder 呼び出し
  - ※ 編集モード中のみ発火（非干渉モードではそもそもアクティブ化しない）
- [x] P9-1: ZOrderByDesktop データ管理（Dictionary<Guid, List<NoteId>>）(2026-02-07 完了)
  - AddToZOrder / RemoveFromZOrder / SyncZOrderList / SyncAllZOrderLists
  - CreateNote / DeleteNote / DuplicateNote で自動連携
  - LoadAll 後に SyncAllZOrderLists で整合性確保
- [x] P9-2: SetWindowPos による TopMost 内 Z順再構築 (2026-02-07 完了)
  - ApplyZOrder(): 末尾→先頭の順に HWND_TOPMOST で配置（最後=最前面）
  - SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE フラグ
- [x] P9-3: Z順管理ウィンドウ（トレイメニューから開く）(2026-02-07 完了)
  - GongSolutions.Wpf.DragDrop 4.0.0 による D&D 並び替え
  - ドラッグハンドル「⠿」+ 背景色カラーインジケータ + 1行目テキスト表示
  - 空なら「（空）」、長文は末尾省略（50文字 + …）
  - トレイメニュー「📊 Z順管理...」から ShowDialog
- [x] P9-4: D&D 並び替え → 即時反映 (2026-02-07 完了)
  - ObservableCollection.CollectionChanged + DispatcherPriority.Background でデバウンス
  - NoteManager.UpdateZOrder() → ApplyZOrder() + ScheduleSave()
- [x] P9-5: 仮想デスクトップ単位の Z順分離 (2026-02-07 完了)
  - ZOrderByDesktop のキーが DesktopId
  - ZOrderWindow は現在デスクトップの付箋のみ表示
  - HandleDesktopSwitch 後に切替先 VD の Z順を適用
- [ ] **P9-VERIFY: Phase 9 検証**
  - [ ] Z順管理画面で並び替えると付箋の前後が即時変わる
  - [ ] 一覧に1行目テキストが表示される（空は「（空）」）
  - [ ] 上が前面、下が背面の順になっている
  - [ ] Z順が再起動後も維持される
  - [ ] **編集モードで付箋をクリックしても Z順が崩れない**
  - [ ] **複数付箋を交互にクリックしても設定した順序が維持される**

---

## Phase 10: 一時非表示 + ホットキー + 自動起動 ✅ (2026-02-07 完了)
> 目標: 非表示/ホットキー/自動起動の3機能を完成させる
> 実装方針: **案B（バランス）** — 全機能実装、ホットキー設定UIはトレイ簡易版（キー変更は Phase 11）

- [x] P10-1: 一時非表示トグル実装（FR-HIDE）(2026-02-07 完了)
  - NoteManager.SetHidden() — 全付箋 Cloak/Uncloak + VD 連携 + 永続化
  - トレイメニュー「👁 一時的に非表示」/「👁 付箋を再表示」トグル
  - 非表示中のトレイアイコンをグレー化（app_gray.ico）+ ToolTip 変更
  - LoadAll() で非表示状態を復元（FR-HIDE-2: 再起動後も維持）
- [x] P10-2: 編集ON中に非表示 → 強制編集OFF（FR-HIDE-3）(2026-02-07 完了)
  - SetHidden(true) 内で SetEditMode(false) を先に呼ぶ
  - 再表示しても編集OFFのまま
- [x] P10-3: 非表示ON中に新規作成 → モデル作成のみ、表示しない（仕様6.1）(2026-02-07 完了)
  - CreateNote() 内で IsHidden チェック → Show() 後に即 Cloak
  - 非表示中は VD 切替の表示制御もスキップ
- [x] P10-4: ホットキー実装（FR-HOTKEY）(2026-02-07 完了)
  - HotkeyService.cs 新規作成（RegisterHotKey / WM_HOTKEY / オーナーウィンドウにフック）
  - NativeMethods に RegisterHotKey / UnregisterHotKey P/Invoke 追加
  - 既定: Ctrl+Win+E → 編集モードトグル（MOD_NOREPEAT 付き）
  - 非表示中はホットキーを無視（安全設計）
- [x] P10-5: ホットキー設定UI（ON/OFF トレイメニュー）(2026-02-07 完了)
  - トレイメニュー「⌨ ホットキー: ON (Ctrl+Win+E)」/「⌨ ホットキー: OFF」
  - ★ キー変更は Phase 11 設定画面で実装予定
- [x] P10-6: 登録失敗時のエラー表示 (2026-02-07 完了)
  - トレイメニューに「⌨ ホットキー: エラー ⚠」表示
  - ログに詳細エラーコード出力
- [x] P10-7: 自動起動実装（FR-BOOT-1）(2026-02-07 完了)
  - AutoStartService.cs 新規作成（HKCU Run キー読み書き + --autostart フラグ）
  - トレイメニュー「🚀 自動起動: ON ✓」/「🚀 自動起動: OFF」トグル
- [ ] **P10-VERIFY: Phase 10 検証**
  - [ ] トレイから非表示 → 全付箋が消える → 再表示で戻る
  - [ ] 非表示状態で再起動 → 起動後も非表示のまま
  - [ ] 非表示中のトレイアイコンがグレーになっている
  - [ ] 編集ON中に非表示 → 編集OFF + 非表示。再表示しても編集OFF
  - [ ] 非表示中に新規作成 → 再表示後に出現
  - [ ] Ctrl+Win+E で編集ON/OFFが切り替わる
  - [ ] ホットキーを無効化 → キーが効かなくなる
  - [ ] ホットキー登録失敗時にエラーがメニューに出る
  - [ ] 自動起動ON → Windows再起動後にアプリが起動している
  - [ ] 自動起動OFF → 再起動後に起動しない

---

## Phase 11: 設定画面（統合）✅ (2026-02-07 完了)
> 目標: 全設定項目を統合した設定ウィンドウ
> 実装方針: **案B（バランス）** — 4タブ構成、ホットキーはプリセット選択方式（自由キー設定は延期）

- [x] P11-1: 設定ウィンドウ骨格（TabControl + 4タブ XAML）(2026-02-07 完了)
- [x] P11-2: 一般タブ — 自動起動ON/OFF、ホットキー設定（ON/OFF + プリセットドロップダウン）(2026-02-07 完了)
  - プリセット5種: Ctrl+Shift+Alt+E / Ctrl+Shift+F12 / Ctrl+Alt+F11 / Ctrl+Win+N / Ctrl+Shift+F9
  - 登録状態表示（成功/エラー/無効）
- [x] P11-3: フォントタブ — 許可リスト管理（追加/削除）(2026-02-07 完了)
  - システムフォント一覧からの追加 + リストからの削除
  - 変更時に全付箋に即時反映
- [x] P11-4: 付箋管理タブ — Z順一覧（D&D）統合 (2026-02-07 完了)
  - ZOrderWindow と同等の D&D 並び替え機能を設定画面内に統合
- [x] P11-5: 詳細タブ — ログフォルダを開く、診断パッケージ生成（FR-DEBUG）(2026-02-07 完了)
  - ログフォルダ直接オープン
  - 診断パッケージ zip 生成（logs/settings.json/notes.json/environment.txt + オプション RTF）
  - バージョン情報表示
- [x] P11-6: App.xaml.cs 連携（トレイメニュー → 設定画面を開く + 設定同期）(2026-02-07 完了)
  - トレイメニュー簡素化（ホットキー/自動起動/Z順を設定画面に統合）
  - 二重起動防止（既存ウィンドウのアクティブ化）
- [x] P11-7: 設定変更の即時反映 + 永続化 (2026-02-07 完了)
  - 全設定が変更時に ScheduleSave() で即時保存
- [ ] **P11-VERIFY: Phase 11 検証**
  - [ ] 設定画面が開く/閉じる
  - [ ] 各タブの切り替えが動作する
  - [ ] 設定変更が即時反映される
  - [ ] 設定変更が再起動後も維持される
  - [ ] 診断zip が生成でき、中に logs/, settings.json, environment.txt がある
  - [ ] 「本文も含める」OFFでは RTF が zip に含まれない
  - [ ] 「本文も含める」ONでは RTF が zip に含まれる

---

## 🔖 引き継ぎポイント④ — 全機能実装完了
> Phase 9〜11 完了時点で SESSION_HANDOVER を作成
> ここまでで「全FR実装済み + 各Phase検証済み」の状態
>
> **次の枠への指示:**
> 「`要件定義.md`（PRD v0.2.0）、`TODO.md`、本引き継ぎ資料を読んでから作業を開始して」

- [ ] HANDOVER-4: SESSION_HANDOVER_YYYYMMDD_PART4.md 作成
  - 実施内容（詳細）
  - Git コミット履歴
  - TODO進捗状況
  - 各Phase検証結果（P9〜P11-VERIFY の結果）
  - 次回対応すべきこと
  - 現状コードの構成と該当箇所（引用付き）
  - 既知の問題・注意点

---

## Phase 13: VD 紐づけバグ修正（★最優先 — 案B）
> 目標: 仮想デスクトップと付箋の紐づけが外れるバグを修正する
> 背景: SESSION_HANDOVER_20260207_PART10 で報告されたバグ（後回し.md §3）
> 方針: 案B（バランス）— BUG 1/2/3 を一括修正

- [x] P13-1: BUG 1 修正 — DuplicateNote() で DesktopId をコピー (2026-02-07 完了)
  - `model.DesktopId = source.Model.DesktopId;` を追加
  - `AddToZOrder()` に正しい DesktopId で登録されることを確認
- [x] P13-2: BUG 2 修正 — HandleDesktopSwitch() からリアルタイム孤立判定を削除 (2026-02-07 完了)
  - `FindOrphanedDesktopIds` 呼び出し + 救済ループ を削除
  - 起動時の `RescueOrphanedNotes()` のみに限定（こちらは正常動作確認済み）
  - 根拠: 後回し.md §1 にも「リアルタイム救済は動作しない」と記載済み
- [x] P13-3: BUG 3 修正 — CreateNote() で DesktopId 取得のフォールバック強化 (2026-02-07 完了)
  - `GetCurrentDesktopIdFast()` が null → 重量級 `GetCurrentDesktopId()` にフォールバック
  - それでも null → ログ Warning 出力（Guid.Empty のまま = 全VD表示の安全側）
- [ ] **P13-VERIFY: VD バグ修正検証**
  - [ ] 付箋を複製 → 複製元と同じデスクトップに所属する
  - [ ] 複製した付箋が設定画面の付箋管理タブに表示される
  - [ ] VD 切替を繰り返しても他デスクトップの付箋が勝手に移動しない
  - [ ] 新規作成した付箋が正しいデスクトップに紐づく
  - [ ] 再起動後も紐づけが維持される

---

## Phase 14: UI改善（付箋管理 + 非表示ホットキー）— 案B
> 目標: 付箋管理画面の利便性向上 + 非表示トグルのホットキー追加
> 方針: 案B — 3機能を一括実装

- [x] P14-1: 名称変更 — 「Z順管理」→「並び順管理」(2026-02-07 完了)
- [x] P14-2: 付箋管理画面に削除ボタン（🗑）追加 (2026-02-07 完了)
  - 確認ダイアログ付き
  - 削除後にリスト自動更新
- [x] P14-3: 非表示/表示トグルのホットキー追加 (2026-02-07 完了)
  - AppSettings に HideHotkey 追加
  - HotkeyService を複数ホットキー対応に拡張
  - 設定画面の一般タブに非表示ホットキー設定UI追加
  - プリセット選択方式（5種）
- [ ] **P14-VERIFY: UI改善検証**
  - [ ] 「並び順管理」の名称に変わっている
  - [ ] 付箋管理画面のゴミ箱ボタンで付箋が削除される
  - [ ] 非表示ホットキーで付箋の表示/非表示が切り替わる
  - [ ] 設定が再起動後も維持される

---

## Phase 15: デバッグメニュー設定制御 + テキスト配置機能 + 後回し.md整理
> 目標: デバッグメニューの表示制御、テキスト配置（水平3方向+垂直2方向）、後回し.md整理
> 方針: 案B（バランス）

- [ ] P15-1: デバッグメニューの設定制御（案B — チェックボックス + リアルタイム再構築）
  - AppSettings に DebugMenuEnabled: bool（デフォルト: false）追加
  - SettingsWindow 詳細タブに CheckBox 追加
  - App.xaml.cs でトレイメニュー構築時に DebugMenuEnabled で表示/非表示切替
  - 変更時はトレイメニューの再構築（CreateTrayContextMenu 再呼び出し）
- [ ] P15-2: テキスト配置機能（案B — 水平3方向 + 垂直2方向）
  - 水平: 左揃え/中央揃え/右揃え（段落単位、Paragraph.TextAlignmentProperty）
  - 垂直: 上揃え/中央揃え（付箋単位、NoteStyle に VerticalTextAlignment 追加）
  - ツールバーに配置ボタン3つ + 下部バーに垂直配置ボタン
  - 水平はRTF内に含まれるため追加保存不要、垂直はNoteStyleで保存
- [ ] P15-3: 後回し.md §3 を Phase 13 修正済みに更新
- [ ] **P15-VERIFY: Phase 15 検証**
  - [ ] デバッグメニューがデフォルトで非表示
  - [ ] 設定画面で ON にするとトレイにデバッグ項目が表示される
  - [ ] 左揃え/中央揃え/右揃え がツールバーから操作可能
  - [ ] 垂直配置（上/中央）が動作する
  - [ ] 配置設定が再起動後も復元される
  - [ ] 後回し.md §3 が修正済みに更新されている

---

## Phase 12: 統合テスト + 回帰テスト + ポリッシュ
> 目標: 全Phase横断で結合動作を検証し、品質を仕上げる
> ※ 各Phase個別の検証は完了済み。ここでは **Phase間の結合** と **エッジケース** を検証する

### 12A: クロスPhase結合テスト
- [ ] P12-1: リッチテキスト + 永続化の結合
  - 文字装飾した状態で再起動 → RTF内容が完全一致で復元
- [ ] P12-2: スタイル + 永続化の結合
  - 背景色/不透明度/フォントを変更 → 再起動 → 一致復元
- [ ] P12-3: マルチモニタ + 仮想デスクトップの結合
  - 異なるデスクトップの異なるモニタに配置 → 再起動 → 正しい場所に復元
- [ ] P12-4: Z順 + 仮想デスクトップの結合
  - Desktop A と Desktop B でそれぞれ Z順を設定 → 再起動 → 各々が維持
- [ ] P12-5: 非表示 + 新規作成 + 仮想デスクトップの結合
  - 非表示中に別デスクトップで新規作成 → 再表示 → 正しいデスクトップで表示
- [ ] P12-6: ホットキー + モード + 全付箋の結合
  - 複数付箋（異なるスタイル）がある状態で Ctrl+Win+E → 全付箋一括切替
- [ ] **P12-7: Z順 + 編集モード + クリックの結合** ← NEW
  - Z順設定 → 編集ON → 付箋を交互にクリック → Z順が維持されている

### 12B: エッジケース + 例外テスト
- [ ] P12-8: フルスクリーン排他上での挙動（仕様明記どおりベストエフォート）
- [ ] P12-9: セキュアデスクトップ（UAC）→ 復帰後に再表示確認
- [ ] P12-10: 他の TopMost アプリとの競合挙動
- [ ] P12-11: 設定ファイル破損からの復旧（notes.json / settings.json 両方）
- [ ] P12-12: 全付箋削除 → アプリが正常に動き続ける
- [ ] **P12-13: Windows ログオフ / シャットダウン経路での保存** ← NEW
  - タスクマネージャからの強制終了ではなく、正常シャットダウンで保存が通ること
- [ ] **P12-14: 表示スケール変更 → 再起動 → 位置復元** ← NEW
- [ ] **P12-15: Explorer.exe 再起動後のトレイ復帰** ← NEW（余裕があれば）
  - TaskbarCreated メッセージでトレイアイコン再登録
- [ ] **P12-16: 二重起動の排他** ← NEW
  - 自動起動済みの状態で手動起動 → 2つ目が終了し、既存が前面化

### 12C: 回帰テスト（全Phase受け入れ条件の再確認）
- [ ] P12-17: PRD §8 テスト計画に基づく全項目チェック
  - §8.1 起動/常駐
  - §8.2 非干渉/編集
  - §8.3 付箋操作
  - §8.4 見た目
  - §8.5 保存復元
  - §8.6 マルチモニタ
  - §8.7 仮想デスクトップ
  - §8.8 例外

### 12D: ポリッシュ
- [ ] P12-18: エッジケース修正
- [ ] P12-19: パフォーマンス確認（付箋10枚以上での動作）
- [ ] P12-20: アイコン/UIの最終調整

---

## 🔖 引き継ぎポイント⑤ — リリース準備完了
> Phase 12 完了時点で最終 SESSION_HANDOVER を作成
>
> **次の枠への指示（もし v0.3 等へ続く場合）:**
> 「`要件定義.md`（PRD v0.2.0）、`TODO.md`、本引き継ぎ資料を読んでから作業を開始して」

- [ ] HANDOVER-5: SESSION_HANDOVER_YYYYMMDD_PART5.md 作成（最終版）

---

## 進捗サマリ

| Phase | 内容 | 状態 |
|-------|------|------|
| Phase 0 | プロジェクト基盤 | ✅ 完了 (2026-02-06) |
| Phase 1 | トレイ常駐 + 最小付箋 | ✅ 完了 (2026-02-06) |
| Phase 2 | Win32 Interop + モード切替 | ✅ 完了 (2026-02-06) |
| Phase 3 | 移動・リサイズ + 基本UI | ✅ 完了 (2026-02-07) |
| Phase 3.5 | 仮想デスクトップ技術スパイク | ✅ 完了 (2026-02-07) |
| Phase 3.7 | DJ-7: オーナーウィンドウ方式変更 | ✅ 完了 (2026-02-07) |
| Phase 4 | リッチテキスト編集 | ✅ 完了 (2026-02-07) |
| Phase 5 | 永続化 | ✅ 完了 (2026-02-07) |
| Phase 6 | 見た目・スタイル | ✅ 完了 (2026-02-07) |
| Phase 7 | マルチモニタ | 未着手 |
| Phase 8.0 | VD 自前管理 技術スパイク | ✅ 完了 (2026-02-07) |
| Phase 8 | 仮想デスクトップ | ✅ 完了 (2026-02-07) |
| Phase 9 | Z順管理 | ✅ 完了 (2026-02-07) |
| Phase 10 | 非表示 + ホットキー + 自動起動 | ✅ 完了 (2026-02-07) |
| Phase 11 | 設定画面 | ✅ 完了 (2026-02-07) |
| Phase 13 | VD 紐づけバグ修正 | ✅ 完了 (2026-02-07) |
| Phase 14 | UI改善（付箋管理 + 非表示ホットキー） | ✅ 完了 (2026-02-07) |
| Phase 15 | デバッグメニュー設定制御 + テキスト配置 | 🔨 作業中 |
| Phase 12 | 統合テスト + 回帰テスト + ポリッシュ | 未着手 |
