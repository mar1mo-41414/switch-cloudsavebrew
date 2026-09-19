# 技術メモ

実装の背景や、ハマりどころだった技術的な発見のまとめ。普段使う分には読む必要はない。
中身を改造したい人・同じ問題にハマった人向け。

## NAX0復号は基本的に不要

実機のJKSVでエクスポートしたセーブデータを実際に調べたところ、JKSVは公式のセーブ
マウントAPI経由でセーブを開いてコピーしており、**NAX0で暗号化された生バイナリでは
なく、既に復号済みの個別ファイル**として書き出されることが分かった。またRyujinxの
実セーブフォルダ(`bis/user/save/<save-id>/0/`)も同じく個別ファイルがそのまま並ぶ
構造(`DirectorySaveDataFileSystem`)であることを確認した。

つまり**JKSV相当の方式でSwitch側アプリを作る場合、NAX0復号/暗号化は不要**。同期の
メイン経路は「復号済みファイルをそのままコピーする」というシンプルな設計にしている。
`LibHacSaveDataCodec`はSDカードの生キャッシュを直接扱う将来の拡張用として温存してある
(通常は使わない)。

さらに、実機JKSVエクスポートとEdenの単一ファイル形式セーブを直接バイト比較したところ
ヘッダーのマジック値が完全一致することを確認した。つまり**実機・Ryujinx・Edenは同じ
「ゲームが定義する復号済みセーブファイル」を扱っており、同一の同期経路で3者間の
相互同期が成立する**。

## BCATは同期対象外

BCAT(サーバー配信キャッシュ)は同期対象外にしている。Ryujinxでは同一title_idでも
BCAT用に別のsave-idが作られる場合があり(`ExtraData`のSaveDataAttribute.Type=2)、
これはプレイヤーの進行データではないため、正しく除外する必要がある。実機側も
`FsSaveDataType_Bcat`をフィルタで除外している。

## Device型セーブ(どうぶつの森の島データ等)

`Account`型(プロフィール別)とは別に、`Device`型(アカウント非依存、1コンソールに
1つ)のセーブが存在する。マウントAPIが異なる(`fsdevMountDeviceSaveData`、ReadOnly版が
存在しない)ため、実機側・PC側どちらでも別扱いにしている。

## Ryujinxのduplex構造(`0/`・`1/`)

Ryujinxの実セーブは電源断対策の2重化構造(`0/`・`1/`の2コピー)になっている。
どちらが最新かを示すcommit-idのバイナリ解析はしていないため、push時はファイル更新
日時が新しい方を正本として選ぶ簡易判定にしている(`DirectoryMirror.PickDuplexSource`)。
pull時は存在するコピー全てへ同一内容を書き込むため(`DirectoryMirror.MirrorDuplex`)、
この簡易判定が問題になることはない。

## mkdirは1階層しか作らない

実機側(libnx/POSIX)の`mkdir`は1階層しか作らず、親ディレクトリが無いと黙って
失敗する。pullでダウンロードしたファイルをステージング領域へ書き込む際、
中間ディレクトリを`mkdir_p`相当のヘルパーで事前に作っておく必要がある。

## Giteaのcontents APIのPOST/PUTの違い

Giteaのcontents APIは新規ファイル作成に`POST`、既存ファイルの更新に`PUT`(sha必須)を
要求する。GitHubのcontents APIはPUT一本で新規/更新どちらも扱えるため、この違いに
最初ハマった(常に`PUT`を使っていて新規パスでHTTP 422 `[SHA]: Required`)。

## 大きいセーブファイルでのHTTPタイムアウト

素朴な低速検出(CURLOPT_LOW_SPEED_LIMIT/TIME)は、実機Wi-Fiの瞬間的な速度低下や
サーバー側処理の一時的な間で誤検知しうる。フラットなタイムアウト値のみに単純化する
方が実機では安定した。

## 実機での日本語表示

`consoleInit`の組み込みビットマップフォントはLatin/拡張ASCII相当のグリフしか
持たず、日本語(かな・漢字)を含む非ASCII文字は文字化けする。これを根本解決する
ため、`consoleInit`/`printf`/`consoleUpdate`を`text_render.c`の独自実装
(`textRenderInit`/`textRenderPrintf`/`textRenderPresent`)に置き換えた:

- `plGetSharedFontByType(PlSharedFontType_Standard)`で、実機のOS自身が日本語UIに
  使っているのと同じ共有システムフォントを取得(追加のフォントファイル同梱は不要)
- [stb_truetype](https://github.com/nothings/stb)(public domain、
  `switch-app/source/vendor/`に同梱)でこのフォントからグリフをラスタライズ
- `Framebuffer` API(`framebufferCreate`+`framebufferMakeLinear`、consoleInit自体と
  同じく1280x720の固定仮想解像度でコンポジタにスケーリングを任せる)へ直接
  ピクセル書き込み
- グリフはコードポイント単位でキャッシュ(固定配列、線形探索)し、毎フレーム
  再ラスタライズしない

API(`textRenderClear`/`textRenderPrintf`/`textRenderPresent`)はconsole系と概ね
1:1対応するよう設計してあり、呼び出し側(`main.c`)は関数名の置換だけで済んでいる。

## 英語/日本語の切り替え(.resx/ResourceManagerを使わない理由)

.NETの標準的なローカライズ手段は`.resx`+`ResourceManager`(satelliteアセンブリ
方式)だが、これはカルチャごとに別ファイル(別dll)を生成する仕組みのため、
`dotnet publish -p:PublishSingleFile=true`での自己完結シングルファイル配布と
相性が悪い(satelliteアセンブリを単一ファイルへ正しく埋め込むには追加設定が
必要で、ビルドパイプラインが複雑になる)。

そのため、PC側は`SwitchCloudSaveBrew.Core/L.cs`に`L.Pick(en, ja)`という自前の
最小限の仕組みを用意した。switch-app側も同じ考え方で`l10n.h`の`L(en, ja)`
インライン関数を使っている。どちらも「呼び出し箇所に英語と日本語を両方書く」
という素朴な方式だが、追加の依存やビルド設定変更が不要で、実装も見通しやすい。
