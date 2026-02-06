# TopFusen — Windows 付箋オーバーレイツール

**TopFusen** は、Windows デスクトップ上に常時前面表示の付箋（メモ）を配置するツールです。  
「このウィンドウは何をしているか」を忘れないためのラベルとして使えます。

## 特徴

- **常時前面表示**: 付箋は常に他のウィンドウより前面に表示
- **非干渉モード**: クリック透過で他アプリの操作を邪魔しない
- **リッチテキスト**: 文字単位の装飾（太字・色・サイズ・下線・取り消し線）
- **マルチモニタ対応**: モニタごとの配置を保存・復元
- **仮想デスクトップ対応**: Windows 11 の仮想デスクトップ単位で付箋を管理
- **タスクトレイ常駐**: 最小限のUIでバックグラウンド動作

## 動作環境

- **OS**: Windows 10 / 11
- **ランタイム**: .NET 8.0
- **フレームワーク**: WPF

## ビルド手順

### 前提条件

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) がインストール済み

### ビルド

```powershell
# リポジトリのクローン
git clone <repository-url>
cd TopFusen

# ビルド
dotnet build TopFusen.sln

# 実行
dotnet run --project TopFusen/TopFusen.csproj
```

### デバッグ実行（Visual Studio / Rider）

1. `TopFusen.sln` を開く
2. TopFusen プロジェクトをスタートアッププロジェクトに設定
3. F5 で実行

## プロジェクト構成

```
TopFusen/
├── TopFusen.sln          # ソリューションファイル
├── TopFusen/             # WPF アプリケーション
│   ├── Models/           # データモデル（NoteModel, AppSettings）
│   ├── Views/            # WPF ビュー（XAML）
│   ├── ViewModels/       # ViewModel
│   ├── Services/         # ビジネスロジック・サービス
│   ├── Interop/          # Win32 P/Invoke ラッパー
│   ├── Assets/           # アイコン・リソース
│   ├── App.xaml          # アプリケーション定義
│   └── app.manifest      # DPI / UAC マニフェスト
├── 要件定義.md            # PRD v0.2.0
├── TODO.md               # 全体実装計画
└── README.md             # 本ファイル
```

## データ保存先

```
%LocalAppData%\TopFusen\TopFusen\
├── settings.json         # アプリ設定
├── settings.json.bak     # バックアップ
├── notes.json            # 付箋メタデータ
├── notes.json.bak        # バックアップ
├── notes\                # RTF 本文
│   └── {NoteId}.rtf
└── logs\                 # ログファイル（7日ローテーション）
    └── app_yyyyMMdd.log
```

## ライセンス

Private
