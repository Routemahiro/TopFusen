# SESSION_HANDOVER_20260207_PART9

**作成日**: 2026-02-07
**対象セッション**: Phase 9 Z順管理（FR-ZORDER）実装完了 + Phase 10 着手準備

---

## 1. 今回実施した内容

### Phase 9: Z順管理（FR-ZORDER）— 案C（積極的）一括実装 ✅

GongSolutions.Wpf.DragDrop 4.0.0 を導入し、D&D付きZ順管理ウィンドウ + Z順ロジックを一括実装。ビルド成功・Lint クリーン確認済み。

#### P9-0: Z順固定ポリシー（DJ-2）
- **ポリシー**: クリック/アクティブ化で Z順を変えない（設定D&Dのみ）
- `NoteManager.OnNoteActivated()` → `SelectNote` + `ApplyZOrder` で Z順を即座に再適用
- `SetEditMode()` の編集ON遷移後にも `ApplyZOrder()` 呼び出し
- `HandleDesktopSwitch()` の末尾にも `ApplyZOrder(currentDesktopId)` 呼び出し
- Windows がアクティブ化でZ順を変えても、SetWindowPos で即座にリセット

#### P9-1: ZOrderByDesktop データ管理
- **`AddToZOrder(NoteModel)`**: 新規/複製付箋を Z順リストの最前面（index 0）に追加
- **`RemoveFromZOrder(Guid)`**: 削除時に全 Z順リストから除去
- **`SyncZOrderList(Guid)`**: 指定VDのリストと実際の付箋を同期（漏れ追加/孤立除去）
- **`SyncAllZOrderLists()`**: 全VD一括同期（起動時 `LoadAll` で呼ぶ）
- `CreateNote` / `DeleteNote` / `DuplicateNote` / `LoadAll` で自動連携済み

#### P9-2: SetWindowPos による Z順再構築
- **`ApplyZOrder(Guid? desktopId = null)`**: 公開メソッド
  - ZOrderByDesktop[desktopId] の並び順: **index 0 = 最前面、末尾 = 最背面**
  - 末尾→先頭の順に `SetWindowPos(HWND_TOPMOST)` で配置（最後に配置=最前面）
  - `SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE` フラグ
  - desktopId 省略時は現在VDを自動取得

#### P9-3: Z順管理ウィンドウ（ZOrderWindow）
- **新規ファイル**: `Views/ZOrderWindow.xaml` + `Views/ZOrderWindow.xaml.cs`
- GongSolutions.Wpf.DragDrop 4.0.0 による D&D 並び替え
  - `dd:DragDrop.IsDragSource="True"` + `dd:DragDrop.IsDropTarget="True"`
  - `dd:DragDrop.UseDefaultDragAdorner="True"` でドラッグ中のビジュアル表示
- 各項目: ドラッグハンドル「⠿」+ 背景色カラーインジケータ（Ellipse）+ 1行目テキスト
- 空付箋は「（空）」、長文は50文字 + 「…」で省略
- トレイメニュー「📊 Z順管理...」から `ShowDialog` で開く
- ヘッダーに「Z順管理 — {デスクトップ名}」を表示

#### P9-4: D&D → 即時反映
- `ObservableCollection<ZOrderItem>.CollectionChanged` で変更検知
- GongSolutions.Wpf.DragDrop は Remove + Insert の2回発火するため、
  `DispatcherPriority.Background` で操作完了後に1回だけ処理
- `NoteManager.UpdateZOrder()` → `ApplyZOrder()` + `ScheduleSave()`

#### P9-5: 仮想デスクトップ単位の Z順分離
- `AppSettings.ZOrderByDesktop` のキーが `DesktopId`（既存の Dictionary 定義を活用）
- `ZOrderWindow` は現在VDの付箋のみ表示
- `HandleDesktopSwitch` 後に切替先VDの Z順を適用

---

## 2. Git コミット履歴

```
395c759 docs: Phase 9 TODO update and progress log
15766a2 feat(zorder): Phase 9 - Z-order management with drag-and-drop UI
```

---

## 3. TODO 進捗状況

| Phase | 内容 | 状態 |
|-------|------|------|
| Phase 0 | プロジェクト基盤 | ✅ 完了 |
| Phase 1 | トレイ常駐 + 最小付箋 | ✅ 完了 |
| Phase 2 | Win32 Interop + モード切替 | ✅ 完了 |
| Phase 3 | 移動・リサイズ + 基本UI | ✅ 完了 |
| Phase 3.5 | 仮想デスクトップ技術スパイク | ✅ 完了 |
| Phase 3.7 | DJ-7: オーナーウィンドウ方式 | ✅ 完了 |
| Phase 4 | リッチテキスト編集 | ✅ 完了 |
| Phase 5 | 永続化 | ✅ 完了 |
| Phase 8.0 | VD 自前管理 技術スパイク | ✅ 完了 |
| Phase 8 | 仮想デスクトップ | ✅ 完了 |
| Phase 6 | 見た目・スタイル | ✅ 完了 |
| **Phase 9** | **Z順管理** | **✅ 完了** |
| Phase 7 | マルチモニタ復元強化 | ⏸ 後回し（後回し.md 参照） |
| **Phase 10** | **非表示 + ホットキー + 自動起動** | **★ 次はここ** |
| Phase 11 | 設定画面 | 未着手 |
| Phase 12 | 統合テスト + ポリッシュ | 未着手 |

---

## 4. 次回対応すべきこと

### Phase 10: 一時非表示 + ホットキー + 自動起動

TODO.md の Phase 10 セクションに詳細タスクリストあり:

- **P10-1**: 一時非表示トグル実装（FR-HIDE）
  - トレイメニューから全付箋 Show/Hide
  - 非表示状態の永続化（`AppSettings.IsHidden` → `settings.json`）
  - 非表示中のトレイアイコンをグレー化
  - **注意**: 現在 `App.xaml.cs` のトレイメニューに stub がある（「👁 一時的に非表示」）
- **P10-2**: 編集ON中に非表示 → 強制編集OFF（FR-HIDE-3）
- **P10-3**: 非表示ON中に新規作成 → モデル作成のみ、表示しない（仕様6.1）
- **P10-4**: ホットキー実装（FR-HOTKEY）
  - `RegisterHotKey` / `WM_HOTKEY` で編集モードトグル
  - 既定: Ctrl+Win+E
  - `AppSettings.Hotkey` は既に定義済み（Modifiers=0x0003, Key=0x45）
- **P10-5**: ホットキー設定UI（変更 / 無効化）
  - ★ Phase 11 の設定画面に委ねるか、簡易UIを先行するか判断が必要
- **P10-6**: 登録失敗時のエラー表示（設定画面）
- **P10-7**: 自動起動実装（FR-BOOT-1）
  - `HKCU\...\Run` への登録/解除
  - `--autostart` フラグ対応
  - `AppSettings.AutoStartEnabled` は既に定義済み

### 実装の注意点

- `NoteManager` に非表示の Show/Hide 管理メソッドを追加する必要がある
- ホットキー登録は `WM_HOTKEY` をウィンドウメッセージで受け取るため、オーナーウィンドウ or メッセージ専用ウィンドウにフックする
- 自動起動はレジストリ操作なので管理者権限不要（HKCU）
- 非表示状態の起動時復元: `LoadAll` 後に `AppSettings.IsHidden` をチェック → 全付箋を非表示

---

## 5. 現状コードの構成と該当箇所

### Phase 9 で追加/変更したファイル

| ファイル | 変更内容 |
|----------|----------|
| `TopFusen.csproj` | `gong-wpf-dragdrop 4.0.0` パッケージ追加 |
| `Services/NoteManager.cs` | Phase 9: Z順管理セクション追加（170行）+ 既存メソッド7箇所修正 |
| `Views/ZOrderWindow.xaml` | **新規** — D&D付きZ順管理ウィンドウ |
| `Views/ZOrderWindow.xaml.cs` | **新規** — コードビハインド（ZOrderItem クラス含む） |
| `App.xaml.cs` | トレイメニューに「📊 Z順管理...」追加 |

### Phase 10 に関連する既存コード

| ファイル | 箇所 | 役割 |
|----------|------|------|
| `Models/AppSettings.cs` | `IsHidden`, `Hotkey`, `AutoStartEnabled` | 設定モデル（全て定義済み） |
| `Models/AppSettings.cs` | `HotkeySettings` クラス | Enabled/Modifiers/Key（既定値設定済み） |
| `App.xaml.cs` | `CreateTrayContextMenu()` | 非表示 stub + トレイメニュー構成 |
| `App.xaml.cs` | `_trayIcon` | H.NotifyIcon.Wpf の TaskbarIcon インスタンス |
| `Services/NoteManager.cs` | `SetEditMode()` | 編集モード切替（ホットキーから呼ぶ） |
| `Services/NoteManager.cs` | `_ownerWindow` | ホットキー WM_HOTKEY フックの候補ウィンドウ |
| `Interop/NativeMethods.cs` | （未定義） | RegisterHotKey / UnregisterHotKey の P/Invoke が必要 |

### NoteManager.cs の Phase 9 Z順管理セクション構成

```
行 733-900 付近:
  GetOrCreateZOrderList()   — Z順リスト取得/作成
  AddToZOrder()             — 最前面追加
  RemoveFromZOrder()        — 全リスト除去
  SyncZOrderList()          — 単一VD同期
  SyncAllZOrderLists()      — 全VD同期
  ApplyZOrder()             — SetWindowPos でZ順適用
  UpdateZOrder()            — 外部からの更新（ZOrderWindow用）
  GetCurrentDesktopId()     — 現在VD取得
  GetDesktopName()          — VD名取得
  GetOrderedNotesForDesktop() — Z順付箋リスト取得
```

### Z順が適用されるタイミング（全7箇所）

1. `OnNoteActivated()` — 付箋クリック時（DJ-2: 崩れ防止）
2. `CreateNote()` — 新規作成時（最前面に追加後）
3. `DuplicateNote()` — 複製時（最前面に追加後）
4. `LoadAll()` — 起動時（SyncAllZOrderLists 後）
5. `SetEditMode()` — 編集ON時（Uncloak 後の順序確定）
6. `HandleDesktopSwitch()` — VD切替時（切替先VDの順序適用）
7. `UpdateZOrder()` — ZOrderWindow の D&D 後

---

## 6. 既知の問題・後回し事項

1. **VD 削除時のリアルタイム救済** — 後回し.md §1 参照
2. **Phase 7 マルチモニタ復元強化** — 後回し.md §2 参照
3. **削除確認ダイアログ未実装** — 即削除のまま
4. **P4-VERIFY / P5-VERIFY / P6-VERIFY / P9-VERIFY** — 実機確認は行っているが TODO 上の正式チェック未完了
5. **ZOrderWindow の制限**: 開いている間に付箋を作成/削除するとリストが同期しない（閉じて開き直しで更新）
   - Phase 11 で設定画面に統合する際にリアルタイム同期を検討

---

## 7. 次の枠への指示

次の3つのファイルを読んでから作業を開始して:
1. `要件定義.md` — PRD v0.2.0
2. `TODO.md` — 全体計画 + 進捗 + 設計判断ログ
3. `引き継ぎ資料置き場/SESSION_HANDOVER_20260207_PART9.md` — 本資料

後回し事項は `引き継ぎ資料置き場/後回し.md` を参照。
