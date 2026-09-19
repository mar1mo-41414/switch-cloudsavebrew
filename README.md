# Switch-CloudSaveBrew

Nintendo Switch実機とSwitchエミュレータ(Ryujinx, Eden等)の間で、セーブデータを
クラウド同期するためのツール群。Steamクラウドセーブのように、実機で遊んだ続きを
PCのエミュレータで、PCで遊んだ続きを実機で、そのまま遊べるようにする。

セーブデータ本体は、このリポジトリとは**別のプライベートなGitリポジトリ**に置く。
このリポジトリ自体にはツールのソースコードだけが入っている。

ビルド済みのバイナリ(PC側CLI/GUIのWindows/Linux/Mac向け実行ファイル、Switch実機用
`.nro`)は[Releases](../../releases)ページから直接ダウンロードできる。自分でビルド
したい場合は下記「セットアップ」を参照。

## できること

- 表示言語は英語/日本語を切り替え可能(既定は英語)

- 実機 ⇔ PC(Ryujinx/Eden、Windows/Linux/Mac)間でのセーブの相互同期
- セーブデータの自動検出(手動でのパス設定なしでも、実機・エミュどちらからでも
  同期対象を見つけられる)
- 複数のNintendoアカウントがある場合の分離対応
- CLIツール(全機能)とAvalonia製の簡易GUI(Push/Pull中心)の両方を提供
- 実機・PC双方で同期を忘れて進めてしまっても、更新が新しい方を正として自動判定

## 構成

- `pc-tool/` — PC側ツール(C#/.NET)。CLIとGUIの両方入り
- `switch-app/` — Switch実機用Homebrewアプリ(詳細は[switch-app/README.md](switch-app/README.md))

## セットアップ

### 1. セーブデータ保管用のGitリポジトリを用意する

ソースコード(このリポジトリ)とは別に、セーブデータ本体を置く**プライベートな**
Gitリポジトリを1つ用意する(Gitea・GitHub・GitLab等)。実機側から同期する場合は
Gitea(または互換API)が必要、PC側ツールのみ使う場合は任意のGitホスティングで動く。

### 2. PC側ツールのセットアップ

.NET 8 SDK以降が必要。

```bash
cd pc-tool
cp ../config.example.yaml config.yaml   # 環境に合わせて編集(config.yamlはgit管理外)
dotnet run --project SwitchCloudSaveBrew.Cli -- config validate config.yaml
```

設定項目は[config.example.yaml](config.example.yaml)にコメント付きで書いてある。

### 3. Switch側アプリのセットアップ(任意)

実機からも同期したい場合のみ。手順は[switch-app/README.md](switch-app/README.md)参照。

## 使い方

```bash
cd pc-tool

# セーブを取り込んでリポジトリへpush
dotnet run --project SwitchCloudSaveBrew.Cli -- sync push config.yaml <title_id> [emulator]

# リポジトリから最新セーブを取得してエミュのセーブフォルダへ配置(上書き前に確認あり)
dotnet run --project SwitchCloudSaveBrew.Cli -- sync pull config.yaml <title_id> [emulator]
```

### 自動検出(手動でtargetを書かなくてよい)

```bash
# エミュのルートディレクトリを指定して、存在するセーブ一覧を確認
dotnet run --project SwitchCloudSaveBrew.Cli -- scan list config.yaml ryujinx ~/.config/Ryujinx

# 検出したセーブをそのままpush/pull
dotnet run --project SwitchCloudSaveBrew.Cli -- scan push config.yaml ryujinx ~/.config/Ryujinx <title_id>

# リポジトリ上にどのゲームのセーブがあるか確認
dotnet run --project SwitchCloudSaveBrew.Cli -- cloud list config.yaml
```

### GUI

CLIをそのまま使いたくない場合向けの、簡素な作りのGUI。Win/Linux/Mac共通コード。

```bash
cd pc-tool
dotnet run --project SwitchCloudSaveBrew.Gui
```

Games(config.yamlのゲーム一覧からPush/Pull)・Scan(自動検出)・Cloud(リポジトリの
中身を参照)の3タブ構成。右上のドロップダウンで表示言語(English/日本語)を切り替え
られる。自分でビルドする場合は、プロジェクトルートの`build.sh`(Linux/Mac)・
`build.ps1`(Windows)で単体実行ファイルを`output/`に生成できる。

## 複数アカウント対応

1台のSwitchに複数のNintendoアカウントがあり、ゲームごとに別々の進行データが
ある場合の対応。config.yamlの`accounts:`セクションで、実機の本物のアカウントと
各エミュレータのローカルプロフィールを紐付ける:

```yaml
accounts:
  - name: "player1"
    switch_uid: "実機の本物のAccountUid(32桁hex)"
    ryujinx_uid: "Ryujinxのそのプロフィールのローカルuid(32桁hex)"
    eden_uid: "Edenのそのプロフィールのローカルuid(32桁hex)"
```

値の調べ方:
- `switch_uid`: switch-appのメイン画面で**[X]**を押すと表示される
- `ryujinx_uid`/`eden_uid`: `scsb scan list`(またはGUIのScanタブ)の出力に表示される

アカウントが1つしかない場合は何も設定しなくてよい。

## 既知の制限: オンライン機能・アカウント連携があるゲーム

セーブデータには「どのニンテンドーアカウントに紐づいているか」の情報が含まれている。
クラウド経由で別の実機/エミュレータ環境(=別アカウント扱い)にそのセーブを持って
いくと、ゲーム側が「知らないアカウントのセーブだ」と判定し、オンライン機能の再
初期化を要求してくることがある。これはNintendo側の仕組みに起因するもので、
ツール側では対処できない。

- オフライン専用・進行データのみのゲーム → 問題なくクラウド同期できる
- オンライン機能やアカウント連携が絡むゲーム(通信対戦・サーバー同期する住民管理系
  ゲーム等) → クラウド同期は基本的に避けるべき

## 対応エミュレータのセーブ形式

| エミュレータ | ローカルの格納形態 | config設定 |
|---|---|---|
| Ryujinx | ディレクトリ(duplex構造) | `is_duplex: true` |
| Eden | 単一ファイル or ディレクトリ(ゲームによる) | ゲームの構造に合わせて設定 |

`is_file`/`is_duplex`はエミュレータそのものというより、ゲームのセーブ構造・
エミュレータの内部実装に対する設定。詳しい技術的背景は
[docs/TECHNICAL_NOTES.md](docs/TECHNICAL_NOTES.md)を参照。

## 設定ファイル

[config.example.yaml](config.example.yaml)を参照。

## ライセンス

MIT
