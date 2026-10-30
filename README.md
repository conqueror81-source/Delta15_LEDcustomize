# Delta15_LEDcustomize (msikblight)

MSI Delta 15 A5EFK などに搭載されている **MysticLight MS-1562** キーボードライティングコントローラ
（USB VID `1462` / PID `1562`〜`1564`）を、ドライバなし・MSI Center なしで制御するツール。GUI と CLI の両方で使える。

- Windows 標準の HID ドライバ（`hid.dll`）経由で Feature Report を送るだけなので、追加ドライバ・テスト署名は不要
- 設定は `%ProgramData%\MsiKbLight\config.json` に保存
- タスクスケジューラ（SYSTEM 権限）で **起動時・ログオン時・スリープ/休止/モダンスタンバイ復帰時** に自動で再適用
- 常駐プロセスなし

## 必要なもの

- .NET 8 SDK（ビルド用）／ .NET 8 Desktop Runtime（実行用、SDK に同梱）

## ビルド

```powershell
dotnet publish src\MsiKbLight -c Release -o publish
```

`publish\` に `msikblight.dll` と `msikblight.cmd` ができる。

### exe を作らない理由（Smart App Control）

Smart App Control が有効な環境では、自分でビルドした署名なしの `msikblight.exe` がブロックされる。
そのため exe（apphost）は生成せず、Microsoft 署名済みの `dotnet.exe` から `msikblight.dll` を起動する。

| 入口 | 実体 |
|---|---|
| `msikblight.cmd`（CLI） | `dotnet.exe msikblight.dll <引数>` |
| `msikblight.cmd`（引数なし）/ スタートメニュー | `conhost.exe --headless dotnet.exe msikblight.dll`（コンソールを出さずに GUI を開く） |
| 自動適用タスク | `dotnet.exe msikblight.dll apply --wait 60`（SYSTEM） |

## GUI

`publish\msikblight.cmd` をダブルクリック（インストール後はスタートメニューの「MSI キーボード ライティング」）。

- モード・色・明るさ・速度を変えると、すぐにキーボードに反映される（プレビュー）
- 「保存」で次回起動時にも使う設定として記録
- 「元に戻す」で保存済み（未保存なら起動時）の状態に戻す
- 「再起動・スリープ復帰時の自動適用」の「有効にする」で、UAC 確認のあとインストールとタスク登録を行う
- MSI Center が動作中の場合は警告を表示

## CLI

```powershell
msikblight get                                   # 現在の設定を読み出し
msikblight set static -c FF0000                  # 赤で点灯（設定を保存）
msikblight set breathing -c 00A0FF -s 0 -b 6     # 呼吸、遅い、明るさ 6
msikblight set colorshift -c FF0000,FFFF00,00FF00,00FFFF,0000FF,FF00FF
msikblight set rainbow
msikblight set off
msikblight apply                                 # 保存済み設定を再適用
msikblight install                               # 自動適用を有効化（管理者）
msikblight uninstall                             # 自動適用を無効化（管理者）
```

| モード | GUI 表記 | 使用する色 |
|---|---|---|
| `off` | 消灯 | - |
| `static` | 点灯（単色） | 1 色 |
| `breathing` | 呼吸 | 1 色 |
| `flashing` | 点滅 | 1 色 |
| `doubleflashing` | 2 回点滅 | 1 色 |
| `colorshift` | カラーシフト | 最大 7 色 |
| `rainbow` | レインボー | - |

- `-b, --brightness` 0〜10（**実機では 9 以上は 8 に丸められる**。GUI は 0〜8）
- `-s, --speed` 0=遅い / 1=普通 / 2=速い
- `--no-save` 適用のみで保存しない

## 自動適用（再起動後も維持）

GUI の「有効にする」、または管理者権限で `msikblight install` を実行すると:

1. `C:\Program Files\MsiKbLight` にファイルをコピー
2. `%ProgramData%\MsiKbLight` に一般ユーザーの書き込み権限を付与（GUI / `set` で保存した設定をタスクが読めるように）
3. タスク `MsiKbLight` を登録（起動時・ログオン時・復帰時に実行）
4. スタートメニューに「MSI キーボード ライティング」を登録

**MSI Center は併用しないこと。** MSI Center（Mystic Light / LightKeeperService）が起動時に独自の設定を書き込み、上書きされる。

## プロトコル

MSI Center の `LEDKeeper2.exe`（`MSI_LED.MSI_1562Led`）の実装に基づく。

| Report | 方向 | 内容 |
|---|---|---|
| `0x02` (64 byte) | Set | `[0]=02 [1]=00 [2]=mode [3]=speed(0-2) [4]=brightness(0-10) [5]=色数(1-7) [6..26]=RGB×7` |
| `0x01` (64 byte) | Set | `01 91 02 CC CC ...` 現在設定の読み出し要求（`01 91 01` は FW バージョン） |
| `0x02` (64 byte) | Get | 上記要求後に現在設定を返す（Set と同じ形式） |

mode: 0=Off 1=Static 2=Breathing 3=Flashing 4=DoubleFlashing 5=ColorShift 6=Rainbow

`0x5A`（ブートローダ移行）と `0x5B`（MCU リセット）コマンドも存在するが、本ツールでは使用しない。
