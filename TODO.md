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

## Phase 2: Win32 Interop + モード切替
> 目標: 非干渉モード（クリック透過）と編集モードが切り替わる
> ★ ここが TopFusen の最重要技術検証ポイント
> ★ 透明Window + クリック透過 + TopMost の共存を検証する

- [ ] P2-1: Win32 Interop ヘルパー作成（Interop/NativeMethods.cs）
  - SetWindowLong / GetWindowLong（GWL_EXSTYLE）
  - SetWindowPos（HWND_TOPMOST）
  - WS_EX_TRANSPARENT / WS_EX_LAYERED / WS_EX_NOACTIVATE / WS_EX_TOOLWINDOW 定数
- [ ] P2-2: NoteWindow にクリック透過の ON/OFF 実装
  - 非干渉: WS_EX_TRANSPARENT + WS_EX_NOACTIVATE ON
  - 編集: WS_EX_TRANSPARENT OFF
- [ ] P2-3: AppHost にモード管理（IsEditMode プロパティ）
  - トレイメニューのトグルと連動
  - 全 NoteWindow に一括適用
- [ ] **P2-VERIFY: Phase 2 検証（★最重要）**
  - [ ] 非干渉モード: 付箋上をクリックすると背後アプリが反応する
  - [ ] 非干渉モード: 付箋がフォーカスを奪わない
  - [ ] 編集モード: 付箋をクリックで選択・操作できる
  - [ ] 編集モード: 付箋以外をクリックしても編集OFFに戻らない
  - [ ] トレイメニューで ON/OFF が正しくトグルする
  - [ ] TopMost が維持されている（他ウィンドウで隠れない）
  - [ ] **AllowsTransparency=True の状態で上記すべてが成立する** ← NEW
  - [ ] **Alt+Tab / タスクバーに付箋が出ない状態が維持されている** ← NEW

---

## Phase 3: 付箋の移動・リサイズ + 基本UI
> 目標: 編集モードで付箋をドラッグ移動・リサイズできる

- [ ] P3-1: WindowChrome 導入（リサイズハンドル有効化）
  - ResizeBorderThickness 設定
  - CaptionHeight=0（カスタムタイトルバー）
- [ ] P3-2: ドラッグ移動実装（ツールバー領域を Caption 扱い）
- [ ] P3-3: リサイズ実装（角/辺ドラッグ）+ MinWidth/MinHeight 制約
- [ ] P3-4: 編集ON時の選択状態管理
  - 付箋クリックで「選択中」
  - 選択中の付箋のみ: ツールバー + 下部アイコン + 枠/影 表示
  - 未選択: 本文のみ
- [ ] P3-5: 付箋の削除ボタン（下部ゴミ箱アイコン）
  - **削除時に RTF ファイル（notes/{NoteId}.rtf）も削除する** ← NEW
- [ ] P3-6: 付箋の複製ボタン（下部コピーアイコン）
  - 複製時 +24px,+24px ずらし
- [ ] P3-7: 編集OFF時にすべてのUI要素（ツールバー/アイコン/枠）を非表示
- [ ] **P3-VERIFY: Phase 3 検証**
  - [ ] ドラッグで付箋を移動できる
  - [ ] 角/辺ドラッグでリサイズできる
  - [ ] 最小サイズ（160×120）以下にならない
  - [ ] 選択中の付箋だけツールバー/アイコン/枠が表示される
  - [ ] 未選択付箋は本文のみ
  - [ ] 削除ボタンで付箋が消える
  - [ ] **削除した付箋がディスク上からも消えている（RTFファイル）** ← NEW
  - [ ] 複製ボタンで +24px ずれた付箋が生成される
  - [ ] 編集OFF時にすべてのUI要素が消える
  - [ ] **AllowsTransparency + WindowChrome でリサイズが正常に動作する** ← NEW

---

## Phase 3.5: 仮想デスクトップ 技術スパイク ← NEW Phase
> 目標: 仮想デスクトップ API の成立を早期検証する（Phase 8 の前倒しリスク軽減）
> ※ 本格実装は Phase 8。ここでは「使えるか」「どう使うか」を検証するだけ

- [ ] P3.5-1: COM Interop — IVirtualDesktopManager の初期化成立確認
  - CLSID_VirtualDesktopManager の CoCreateInstance
  - 失敗時の graceful 無効化パス
- [ ] P3.5-2: 現在デスクトップID取得（短命ウィンドウ方式）の実証
  - 短命 Window を作成 → GetWindowDesktopId → GUID 取得 → Window 破棄
- [ ] P3.5-3: MoveWindowToDesktop で NoteWindow を別デスクトップへ移動できることを確認
- [ ] P3.5-4: Registry からデスクトップ一覧を読む実験（ベストエフォート）
  - VirtualDesktopIDs byte[] → GUID[]
  - デスクトップが1つだけの場合の挙動確認（値が空になるケース）
- [ ] **P3.5-VERIFY: スパイク検証**
  - [ ] COM 初期化が成功する
  - [ ] 現在デスクトップ ID が GUID として取得できる
  - [ ] NoteWindow を Desktop B へ移動すると Desktop A から消える
  - [ ] COM 失敗時にアプリがクラッシュしない（graceful 無効化）
  - [ ] **スパイク結果を progress_log.txt に記録**（成否・制約・注意点）

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

## Phase 4: リッチテキスト編集（FR-TEXT）
> 目標: 付箋内で文字単位の装飾ができる

- [ ] P4-1: NoteWindow に WPF RichTextBox 配置
  - 編集ON + 選択中のみ編集可能
  - 編集OFF時は ReadOnly + 背景透過
- [ ] P4-2: ツールバー実装（上部配置）
  - 太字（Ctrl+B）
  - 下線（Ctrl+U）
  - 取り消し線
  - 文字サイズ変更（ドロップダウン）
  - 文字色変更（パレット）
- [ ] P4-3: 適用ルール実装（FR-TEXT-4）
  - 選択範囲あり → 選択範囲に適用
  - 選択範囲なし → カーソル以後のトグル状態保持
- [ ] P4-4: ツールチップ実装（機能名 + ショートカット表示）
- [ ] P4-5: Undo/Redo（Ctrl+Z / Ctrl+Y）— RichTextBox 標準機能活用
- [ ] P4-6: クリップボード対応（FR-TEXT-6）
  - リッチ貼り付け優先 → プレーンフォールバック
  - 貼り付け時にフォント正規化（付箋フォントに統一）
- [ ] **P4-VERIFY: Phase 4 検証**
  - [ ] 文字入力ができる
  - [ ] 太字/下線/取り消し線が文字単位で適用される
  - [ ] 文字サイズ変更が文字単位で反映される
  - [ ] 文字色変更が反映される
  - [ ] 選択範囲なしの場合、カーソル以後に適用される
  - [ ] ツールチップに機能名+ショートカットが出る
  - [ ] Ctrl+Z / Ctrl+Y が効く
  - [ ] Ctrl+C / Ctrl+V が効く（リッチ→フォールバック）
  - [ ] 貼り付け時にフォントが付箋フォントに正規化される

---

## Phase 5: 永続化（FR-PERSIST）
> 目標: 再起動後に付箋が完全復元される

- [ ] P5-1: Persistence サービス実装
  - 保存先: %LocalAppData%\TopFusen\TopFusen\
- [ ] P5-2: notes.json 保存/読込（全 Note メタデータ）
- [ ] P5-3: notes/{NoteId}.rtf 保存/読込（RTF本文）
- [ ] P5-4: settings.json 保存/読込（AppSettings）
- [ ] P5-5: Atomic Write 実装（tmp → File.Replace → .bak 生成）
- [ ] P5-6: 破損検知 + .bak フォールバック + ユーザー通知
- [ ] P5-7: デバウンス保存（3秒）— テキスト/移動/リサイズ/スタイル変更時
- [ ] P5-8: 終了時の強制フラッシュ（FR-TRAY-5 連携）
- [ ] **P5-9: SessionEnding / Application.Exit フック** ← NEW
  - Windows ログオフ / シャットダウン時にも確実に同期保存
  - Application.Current.SessionEnding += で保存フラッシュ
  - Atomic Write（P5-5）により、保存途中で落ちても破損しない
- [ ] P5-10: 起動時ロード → NoteManager で復元
  - 起動直後は必ず編集OFF（FR-BOOT-2）
- [ ] **P5-11: 削除時のファイル掃除** ← NEW
  - DeleteNote 時: notes.json からエントリ削除 + notes/{NoteId}.rtf 削除 + 即時保存
  - 起動時: notes.json に無い孤立 RTF ファイルを検知 → 削除（ゴミ掃除）
- [ ] **P5-VERIFY: Phase 5 検証**
  - [ ] 付箋を作成して内容を書く → アプリ終了 → 再起動で内容が復元される
  - [ ] 位置/サイズを変更 → 再起動で復元される
  - [ ] 起動直後が必ず編集OFF
  - [ ] %LocalAppData% に JSON/RTF ファイルが生成されている
  - [ ] .bak ファイルが存在する
  - [ ] settings.json を壊す → 起動時に .bak から復旧し通知が出る
  - [ ] デバウンス: 連続変更が1回の保存にまとまる（ログで確認）
  - [ ] **付箋削除 → 再起動 → 復元されない + RTFファイルも消えている** ← NEW
  - [ ] **孤立RTFファイルを手動配置 → 起動時に掃除される** ← NEW

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

## Phase 6: 見た目・スタイル（FR-STYLE / FR-FONT）
> 目標: 背景色パレット + 不透明度 + フォント選択が動く

- [ ] P6-1: カラーパレット定義（データ駆動）
  - ビビッド系 8色 + ナチュラル系 8色（仮定義）
  - Models/Palette.cs
- [ ] P6-2: 背景色選択UI（カテゴリ → 色選択）
- [ ] P6-3: 不透明度スライダー（0〜100、Opacity連動）
- [ ] P6-4: 編集ON時の操作視認性（FR-STYLE-3）
  - 透明度が高くても選択中付箋に枠線/影を必ず表示
- [ ] P6-5: 文字色選択（黒/白 + パレット）
- [ ] P6-6: フォント選択UI
  - PC内フォント一覧取得
  - 許可リストに基づくフィルタリング
- [ ] P6-7: フォント許可リスト管理（設定画面）
  - 初期プリセット定義
  - 追加/削除
  - 永続化（settings.json）
- [ ] P6-8: 貼り付け時のフォント正規化（付箋フォントに統一）
- [ ] **P6-VERIFY: Phase 6 検証**
  - [ ] パレットから背景色を選択すると即時反映
  - [ ] 不透明度スライダーが 0〜100 で動作（0=完全透明、100=不透明）
  - [ ] 不透明度 0〜10 でも編集ON時に枠/影で操作可能
  - [ ] 文字色（黒/白/パレット）が反映される
  - [ ] フォント選択が付箋全体に適用される
  - [ ] 許可リスト外のフォントは選択UIに出ない
  - [ ] 許可リスト変更が保存され次回起動で維持
  - [ ] 背景色/不透明度が再起動後も復元される

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

## Phase 8: 仮想デスクトップ対応（FR-VDSK）
> 目標: デスクトップ単位で付箋が分離・復元される
> ※ Phase 3.5 のスパイクで技術検証済み。ここでは本格実装を行う

- [ ] P8-1: Phase 3.5 のスパイクコードを正式実装へ昇格
  - IVirtualDesktopManager ラッパー（Services/VirtualDesktopService.cs）
  - COM 初期化失敗時の graceful 無効化
- [ ] P8-2: 現在デスクトップID取得の安定実装（短命ウィンドウ方式）
- [ ] P8-3: Registry — デスクトップ一覧取得（ベストエフォート）
  - VirtualDesktopIDs byte[] → GUID[] パース
  - 名前取得（Desktops\{guid}\Name）
  - **取得失敗 → 現在デスクトップのみ扱い** ← 明記
- [ ] P8-4: 付箋作成時に現在デスクトップIDを付与
- [ ] P8-5: 復元時に MoveWindowToDesktop で所属デスクトップへ移動
- [ ] P8-6: デスクトップ喪失フォールバック
  - Desktop 1 へ移動 → 無理なら現在デスクトップ → クランプ
- [ ] **P8-VERIFY: Phase 8 検証**
  - [ ] Desktop A で作成した付箋が Desktop B で見えない
  - [ ] Desktop B で作成した付箋が Desktop A で見えない
  - [ ] 再起動後も所属デスクトップに復元される
  - [ ] デスクトップ削除後に起動 → フォールバックで見失わない
  - [ ] 仮想デスクトップが1つだけの場合でも正常動作
  - [ ] **COM 初期化失敗時にアプリがクラッシュせず、付箋が現在デスクトップで表示される** ← NEW

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

## Phase 9: Z順管理（FR-ZORDER）
> 目標: 設定画面で付箋の前後関係をD&Dで変更でき、クリックで崩れない

- [ ] **P9-0: Z順固定ポリシーの実装** ← NEW（DJ-2 反映）
  - ポリシー: クリック/アクティブ化で Z順を変えない（設定D&Dのみ）
  - NoteWindow の Activated / GotFocus イベントで Z順を再適用
  - 全 NoteWindow を ZOrderByDesktop の順序どおりに SetWindowPos し直す
  - ※ 編集モード中のみ発火（非干渉モードではそもそもアクティブ化しない）
- [ ] P9-1: ZOrderByDesktop データ管理（Dictionary<Guid, List<NoteId>>）
- [ ] P9-2: SetWindowPos による TopMost 内 Z順再構築
  - 後ろ→前の順に挿入で適用
- [ ] P9-3: 設定画面 — Z順一覧UI
  - ドラッグハンドル「＝」+ 1行目テキスト表示
  - 空なら「（空）」、長文は末尾省略
- [ ] P9-4: D&D 並び替え → 即時反映
- [ ] P9-5: 仮想デスクトップ単位の Z順分離
- [ ] **P9-VERIFY: Phase 9 検証**
  - [ ] 設定画面で並び替えると付箋の前後が即時変わる
  - [ ] 一覧に1行目テキストが表示される（空は「（空）」）
  - [ ] 上が前面、下が背面の順になっている
  - [ ] Z順が再起動後も維持される
  - [ ] **編集モードで付箋をクリックしても Z順が崩れない** ← NEW
  - [ ] **複数付箋を交互にクリックしても設定した順序が維持される** ← NEW

---

## Phase 10: 一時非表示 + ホットキー + 自動起動
> 目標: 非表示/ホットキー/自動起動の3機能を完成させる

- [ ] P10-1: 一時非表示トグル実装（FR-HIDE）
  - トレイメニューから全付箋 Show/Hide
  - 非表示状態の永続化（IsHidden → settings.json）
  - 非表示中のトレイアイコンをグレー化
- [ ] P10-2: 編集ON中に非表示 → 強制編集OFF（FR-HIDE-3）
- [ ] P10-3: 非表示ON中に新規作成 → モデル作成のみ、表示しない（仕様6.1）
- [ ] P10-4: ホットキー実装（FR-HOTKEY）
  - RegisterHotKey / WM_HOTKEY で編集モードトグル
  - 既定: Ctrl+Win+E
- [ ] P10-5: ホットキー設定UI（変更 / 無効化）
- [ ] P10-6: 登録失敗時のエラー表示（設定画面）
- [ ] P10-7: 自動起動実装（FR-BOOT-1）
  - HKCU\...\Run への登録/解除
  - --autostart フラグ対応
- [ ] **P10-VERIFY: Phase 10 検証**
  - [ ] トレイから非表示 → 全付箋が消える → 再表示で戻る
  - [ ] 非表示状態で再起動 → 起動後も非表示のまま
  - [ ] 非表示中のトレイアイコンがグレーになっている
  - [ ] 編集ON中に非表示 → 編集OFF + 非表示。再表示しても編集OFF
  - [ ] 非表示中に新規作成 → 再表示後に出現
  - [ ] Ctrl+Win+E で編集ON/OFFが切り替わる
  - [ ] ホットキーを別キーに変更 → 新キーで動作する
  - [ ] ホットキーを無効化 → キーが効かなくなる
  - [ ] ホットキー登録失敗時にエラーが設定画面に出る
  - [ ] 自動起動ON → Windows再起動後にアプリが起動している
  - [ ] 自動起動OFF → 再起動後に起動しない

---

## Phase 11: 設定画面（統合）
> 目標: 全設定項目を統合した設定ウィンドウ

- [ ] P11-1: 設定ウィンドウ骨格（タブ or セクション構成）
- [ ] P11-2: 一般タブ — 自動起動ON/OFF、ホットキー設定
- [ ] P11-3: フォントタブ — 許可リスト管理
- [ ] P11-4: 付箋管理タブ — Z順一覧（D&D）
- [ ] P11-5: 詳細タブ — ログ閲覧、診断パッケージ生成（FR-DEBUG）
- [ ] P11-6: 診断パッケージ生成実装
  - zip 内容: logs/, settings.json, notes.json, environment.txt
  - 付箋本文（RTF）は既定で含めない（チェックで任意）
- [ ] P11-7: 設定変更の即時反映 + 永続化
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
