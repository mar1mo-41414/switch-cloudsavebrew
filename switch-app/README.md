# switch-app

Switch実機用Homebrewアプリ(libnx)。JKSVと同じ公式セーブマウントAPI
(`fsdevMountSaveData`)経由でセーブを読み書きし、Gitea REST API(git wire
プロトコルは使わない)で `switch-savedata` リポジトリと同期する。

## ビルド

devkitPro公式のapt配布元(apt.devkitpro.org)が一部のネットワーク環境から
アクセスできない場合があるため、Docker Hub経由の公式devkitA64イメージで
ビルドする方式にしている(devkitPro本体をホストにインストールする必要はない)。

```bash
./build.sh
```

`switch-cloudsavebrew.nro` が生成される。

## 実機動作確認済み

以下、実機(Nintendo Switch)で動作確認済み:

- `config.ini`読み込み、アカウント解決(`findAccountUidsForTitle`/`listAllSystemAccounts`、
  複数アカウント対応済み — 詳細は下記「複数アカウント対応」参照)
- curl+mbedtlsでのHTTPS通信(cacert.pem込み、Gitea REST APIとのやり取り)
- `sync pull`: Gitea → 実機のセーブへ配置 → ゲームが正しく読み込み
- `sync push`: 実機のセーブ(`fsdevMountSaveDataReadOnly`) → Gitea → 別PC(エミュレータ)
  の`sync pull`で正しく読み込み
- 単一ファイル形式・複数ファイル/ディレクトリ形式(空フォルダ含む)どちらのセーブ構造も
  実機↔PC間で問題なく同期できることを確認済み

ハマった技術的な話は[../docs/TECHNICAL_NOTES.md](../docs/TECHNICAL_NOTES.md)にまとめてある。

## 複数アカウント対応

同一タイトルに複数アカウント分のセーブがある場合、ゲーム選択後にどのアカウントを
対象にするか選択画面が出る(該当が1つだけなら自動選択、これまで通り何も変わらない)。
メイン画面で**[X]**を押すと、その端末上の全アカウントのニックネームと実機の本物の
AccountUid(32桁hex)を一覧表示する(PC側config.yamlの`accounts:`セクションに
`switch_uid`として書き写すためのデバッグ画面)。詳細はプロジェクトルートの
[README.md](../README.md#複数アカウント対応)参照。

## セットアップ(実機側)

SDカードに以下を配置する:

```
sdmc:/switch/switch-cloudsavebrew.nro          <- ビルド成果物
sdmc:/switch/switch-cloudsavebrew/cacert.pem   <- sd-assets/cacert.pem をコピー
sdmc:/switch/switch-cloudsavebrew/config.ini   <- sd-assets/config.ini.example を
                                                    コピーして編集(token等)
```

`config.ini`のtitle_idは、PC側`config.yaml`の同じゲームと**大文字小文字を含めて
一致させること**(Giteaのcontents APIはパスの大文字小文字を区別するため)。

## 操作

- 上下: ゲーム選択(`config.ini`の`[games]`に加えて、実機上に実在するセーブデータを
  自動検出したものも`[auto]`表示でリストに並ぶ — `save_discovery.c`)
- A: 選択したゲームのメニューを開く
  - A: Push (実機のセーブをGiteaへ)
  - Y: Pull (Giteaの最新をセーブへ書き戻す。上書き前に確認あり)
  - B: 戻る
- -: 表示言語をEnglish/日本語で切り替え(`config.ini`の`[ui] language=`でも指定可、
  既定は英語)
- +: 終了

自動検出は実機上の全セーブから、通常のプロファイル別セーブと、どうぶつの森の島データ
のようなアカウント非依存の共有セーブを対象にする(サーバー配信キャッシュ等は対象外)。
詳しくは[../docs/TECHNICAL_NOTES.md](../docs/TECHNICAL_NOTES.md)参照。

## アイコン

雲+同期を表す2本のループ矢印、Joy-Con配色(赤/青)の斜め分割背景(`icon.jpg`)。
Makefileの`NROFLAGS`経由で`.nro`に埋め込み済み。

## 未実装(既知)

- エミュレータ側と同様のduplex/ExtraDataレベルでの厳密な整合性チェック
  (通常のセーブ内容は問題なく同期できる想定だが実機未検証)
