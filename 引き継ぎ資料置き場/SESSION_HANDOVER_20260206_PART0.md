# SESSION_HANDOVER_20260206_PART0 — 計画策定セッション

> 作成日: 2026-02-06
> セッション種別: 計画策定（実装前）
> 次回: Phase 0 から実装開始

---

## 1. 今回実施した内容

### 1.1 PRD評価
- ユーザーから提供された PRD v0.2.0（Windows付箋オーバーレイツール）を評価
- 技術実現可能性、要件の明確性、アーキテクチャ設計を多角的に分析
- **結論: 開発開始可能（非常に高品質なPRD）**

### 1.2 TODO全体計画の策定
- 13+1フェーズ（Phase 0〜12 + Phase 3.5 スパイク）に分割した実装計画を作成
- 各Phaseに VERIFY（検証タスク）を埋め込む案Bテスト戦略を採用
- Phase 12 を「統合テスト + 回帰テスト」に特化（個別検証は各Phaseで完了させる）
- 5箇所の引き継ぎポイントを配置

### 1.3 アーキテクチャ決定
- **A案（付箋＝複数Window）を採用**
- B案（ホスト1枚Window内にControl）は仮想デスクトップ・マルチモニタで不利のため不採用
- A案の弱点（Z順がクリックで崩れる）は Activated イベントでの再適用で解決する方針

### 1.4 レビュー指摘の反映（7項目）
外部レビューで指摘された以下のブロッカー・改善点を TODO に反映:

| # | 指摘 | 対応 | 追加先 |
|---|------|------|--------|
| 1 | Alt+Tab に付箋が出る | WS_EX_TOOLWINDOW 付与 | Phase 1（P1-5） |
| 2 | 単一インスタンスがない | Mutex + NamedPipe IPC | Phase 0（P0-9） |
| 3 | Z順がクリックで崩れる | Activated で再適用（固定ポリシー） | Phase 9（P9-0）+ DJ-2 |
| 4 | DPI/座標の単位未定義 | Relative主 + DIP+DpiScale補助 | Phase 7（P7-7）+ DJ-3 |
| 5 | RTF ファイル削除漏れ | 削除時+起動時掃除 | Phase 3（P3-5）/ Phase 5（P5-11） |
| 6 | SessionEnding 未対応 | SessionEnding フック | Phase 5（P5-9） |
| 7 | 仮想デスクトップ後回しリスク | 技術スパイク前倒し | Phase 3.5（新設） |

### 1.5 設計判断ログ（DJ-1〜4）
TODO 冒頭に設計判断の根拠を記録するセクションを新設:

- **DJ-1**: A案（複数Window）維持の理由
- **DJ-2**: Z順ポリシー — クリックで前面化しない（固定方式）
- **DJ-3**: 座標保存の単位ルール（Relative主、DIP+DpiScale補助）
- **DJ-4**: 仮想デスクトップ COM 呼び出しスレッド方針

---

## 2. Git コミット履歴

```
dda966a docs: PRD v0.2.0 要件定義 + TODO 全体実装計画を作成
(本引き継ぎ資料のコミットがこの後に続く)
```

---

## 3. ファイル構成（現時点）

```
e:\My_Project\TopFusen\
├── 要件定義.md          ← PRD v0.2.0 全文（1066行）
├── TODO.md              ← 全体実装計画（600行、Phase 0〜12 + 3.5）
├── SESSION_HANDOVER_20260206_PART0.md  ← 本ファイル
└── .git/
```

---

## 4. TODO進捗状況

| Phase | 内容 | 状態 |
|-------|------|------|
| Phase 0 | プロジェクト基盤 | **未着手 ← 次回ここから** |
| Phase 1 | トレイ常駐 + 最小付箋 | 未着手 |
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

## 5. 次回対応すべきこと

### 最優先: Phase 0（プロジェクト基盤）
1. **P0-1**: .NET 8 WPF ソリューション作成
   - `dotnet new wpf -n TopFusen --framework net8.0-windows`
   - フォルダ構成: Models/, Views/, ViewModels/, Services/, Interop/, Assets/
2. **P0-2**: app.manifest（PerMonitorV2 + UAC=asInvoker）
3. **P0-3**: App.xaml で ShutdownMode=OnExplicitShutdown
4. **P0-4**: DI コンテナ（Microsoft.Extensions.DependencyInjection）
5. **P0-5**: Serilog ログ基盤
6. **P0-6**: モデル定義（NoteModel / AppSettings）
   - **注意: Placement は DipX/Y + DpiScale（DJ-3 反映済み）**
7. **P0-7**: .gitignore（dotnet テンプレート）
8. **P0-8**: README.md
9. **P0-9**: 単一インスタンス制御（Mutex + NamedPipe）
10. **P0-10**: トレイ実装方式の確定（NuGet パッケージ選定）

### Phase 0 の後は Phase 1〜3 → 3.5 → 引き継ぎポイント①

---

## 6. 仮決め事項（未確定値）

| 項目 | 仮値 |
|------|------|
| Company名 / App名 | `TopFusen` |
| 最小サイズ | 160×120 px |
| デバウンス秒数 | 3秒 |
| ログ保持 | 7日分 / 最大7ファイル |
| 新規付箋の初期サイズ | 240×180 px |
| 新規付箋の初期配置 | プライマリモニタ中央付近（重なりずらし） |
| パレット色 | ビビッド8色 / ナチュラル8色（実装時に仮定義） |
| PerMonitorV2 | 必須 |

---

## 7. 既知の注意点・リスク

1. **PowerShell 環境**: `&&` 演算子が使えない（古い PS バージョン?）。コマンドは `;` で分けるか個別実行
2. **Phase 2 が最重要技術検証**: AllowsTransparency + WS_EX_TRANSPARENT + TopMost + WindowChrome の共存。ここが通らないとアーキ再検討が必要
3. **Phase 3.5（仮想デスクトップスパイク）**: COM 初期化失敗時の graceful 無効化パスを忘れずに
4. **トレイライブラリ**: NuGet の `Hardcodet.NotifyIcon.Wpf` は後継の `H.NotifyIcon` に移行している可能性あり。P0-10 で確定すること
5. **Windows 環境**: ユーザーは Windows 10.0.26100（Windows 11）、PowerShell

---

## 8. 次の枠で最初にやること

> **以下のファイルを読んでから作業を開始してください:**
>
> 1. `要件定義.md` — PRD v0.2.0 全文
> 2. `TODO.md` — 全体実装計画（Phase 0 から着手）
> 3. `SESSION_HANDOVER_20260206_PART0.md` — 本ファイル
>
> 読み終わったら Phase 0 の P0-1 から実装を開始。
