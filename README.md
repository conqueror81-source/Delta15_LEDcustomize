# msikblight

MSI Delta 15 A5EFK などに搭載されている **MysticLight MS-1562** キーボードライティングコントローラ
（USB VID `1462` / PID `1562`〜`1564`）を、ドライバなし・MSI Center なしで制御するコマンドラインツール。

- Windows 標準の HID ドライバ（`hid.dll`）経由で Feature Report を送るだけなので、追加ドライバ・テスト署名は不要
- 設定は `%ProgramData%\MsiKbLight\config.json` に保存
- タスクスケジューラ（SYSTEM 権限）で **起動時・ログオン時・スリープ/休止/モダンスタンバイ復帰時** に自動で再適用

## ビルド

.NET 8 SDK が必要。

```powershell
dotnet publish src\MsiKbLight -c Release -o publish
```

## 使い方

```powershell
msikblight get                                   # 現在の設定を読み出し
msikblight set static -c FF0000                  # 赤で点灯（設定を保存）
msikblight set breathing -c 00A0FF -s 0 -b 6     # 呼吸、遅い、明るさ 6
msikblight set colorshift -c FF0000,FFFF00,00FF00,00FFFF,0000FF,FF00FF
msikblight set rainbow
msikblight set off
msikblight apply                                 # 保存済み設定を再適用
```

| モード | 説明 | 使用する色 |
|---|---|---|
| `off` | 消灯 | - |
| `static` | 点灯 | 1 色 |
| `breathing` | 呼吸 | 1 色 |
| `flashing` | 点滅 | 1 色 |
| `doubleflashing` | 2 回点滅 | 1 色 |
| `colorshift` | 色の切り替え | 最大 7 色 |
| `rainbow` | レインボー | - |

- `-b, --brightness` 0〜10（**実機では 9 以上は 8 に丸められる**。実質の最大は 8）
- `-s, --speed` 0=遅い / 1=普通 / 2=速い
- `--no-save` 適用のみで保存しない

## 自動適用（再起動後も維持）

管理者権限の PowerShell で:

```powershell
.\publish\msikblight.exe install     # C:\Program Files\MsiKbLight にコピー + タスク "MsiKbLight" を登録
.\publish\msikblight.exe uninstall   # タスクを削除
```

`install` 後は、一般ユーザー権限の `msikblight set ...` で保存した設定がそのまま次回起動時にも使われる。

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
