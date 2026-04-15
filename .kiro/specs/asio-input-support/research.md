# リサーチ・設計判断ドキュメント

---
**機能**: `asio-input-support`
**ディスカバリスコープ**: 既存 Unity UPM パッケージへの複雑な統合（Complex Integration）

---

## サマリー

- **機能**: ASIO オーディオ入力サポートを uLipSync パッケージに追加する
- **ディスカバリスコープ**: 既存システムへの拡張（Brownfield Addition）
- **主要な発見事項**:
  - NAudio 2.x の `AsioOut` クラスは `InitRecordAndPlayback(null, channels, sampleRate)` + `Play()` パターンで録音専用モードを実現できる
  - `AsioAudioAvailableEventArgs.GetAsInterleavedSamples(float[] buffer)` の事前確保バッファ受け取りオーバーロードが存在し、コールバック内ヒープ割り当てを回避できる
  - Unity 6000.3.10f1 の Mono 環境では `System.Memory.dll` 等を明示的にバンドルする必要がある（Unity Editor ランタイムが Facades を持たない既知の問題がある）
  - 既存の `uLipSync.cs` の `OnDataReceived(float[], int)` シグネチャはサンプルレートを受け取れないため、破壊的変更が必要

---

## リサーチログ

### NAudio 2.x AsioOut API サーフェス

- **コンテキスト**: ASIO デバイス連携の主要クラスの仕様を確認する必要があった
- **参照元**:
  - [NAudio AsioRecording.md](https://github.com/naudio/NAudio/blob/master/Docs/AsioRecording.md)
  - [NAudio AsioOut.cs ソースコード](https://github.com/naudio/NAudio/blob/master/NAudio.Asio/AsioOut.cs)
  - [ASIO Recording in NAudio（公式ブログ）](https://markheath.net/post/asio-recording-in-naudio)
- **発見事項**:
  - `AsioOut.GetDriverNames()` — `static string[]` を返す。インストール済み ASIO ドライバ名の配列
  - `new AsioOut(string driverName)` — コンストラクタ。名前でドライバを指定して初期化
  - `asioOut.InputChannelOffset` — 入力チャンネルの開始オフセット（int プロパティ）
  - `asioOut.DriverInputChannelCount` — `driver.Capabilities.NbInputChannels` に相当する最大入力チャンネル数
  - `asioOut.InitRecordAndPlayback(IWaveProvider waveProvider, int recordChannels, int recordOnlySampleRate)` — 録音専用モードは第 1 引数 `null`、第 2 引数でチャンネル数、第 3 引数でサンプルレートを指定
  - `asioOut.Play()` — 録音を開始（名称が紛らわしいが録音にも使用する）
  - `asioOut.Stop()` — 録音を停止
  - `asioOut.Dispose()` — アンマネージドリソース解放（必須）
  - `event EventHandler<AsioAudioAvailableEventArgs> AudioAvailable` — バッファ周期ごとに発火するコールバックイベント
  - `AsioAudioAvailableEventArgs.SamplesPerBuffer` — このコールバック周期のサンプル数
  - `AsioAudioAvailableEventArgs.InputBuffers` — チャンネルごとのバッファポインタ配列
  - `AsioAudioAvailableEventArgs.GetAsInterleavedSamples()` — 新規 float[] を生成して返すオーバーロード（毎コールバックでヒープ割り当てが発生するため RT 安全でない）
  - `AsioAudioAvailableEventArgs.GetAsInterleavedSamples(float[] buffer)` — 事前確保した float[] に書き込むオーバーロード（RT 安全）。バッファサイズは `SamplesPerBuffer × InputBuffers.Length` 以上が必要
- **影響**:
  - 本設計では `GetAsInterleavedSamples(float[] buffer)` オーバーロードを使用し、コールバック内ヒープ割り当てをゼロにする
  - `InputChannelOffset` は `AsioOut` 初期化前にプロパティとして設定する必要がある

### ASIO ドライバライフサイクル

- **コンテキスト**: 正しい初期化・停止・破棄順序の確認
- **参照元**: NAudio 公式ドキュメント、GitHub ソースコード
- **発見事項**:
  1. `AsioOut.GetDriverNames()` でドライバ名を列挙（静的呼び出し、ドライバ接続不要）
  2. `new AsioOut(driverName)` でドライバに接続
  3. `asioOut.InputChannelOffset = offset` でチャンネルオフセット設定
  4. `asioOut.InitRecordAndPlayback(null, channelCount, sampleRate)` で初期化
  5. `asioOut.AudioAvailable += handler` でコールバック登録
  6. `asioOut.Play()` で録音開始
  7. コールバック発火（ASIOバッファ周期、通常 64〜4096 サンプル）
  8. `asioOut.Stop()` で録音停止
  9. `asioOut.Dispose()` で非管理リソース解放（省略すると ドライバロックが残る）
- **影響**:
  - `Dispose()` を `finally` ブロックで保証する設計とする
  - `AsioOut` インスタンスは録音の開始・停止のたびに再生成する（再利用は推奨されない）

### Unity 6000.3.10f1 Mono + .NET Standard 2.1 互換性

- **コンテキスト**: NAudio 2.x が依存する `System.Memory`（`Span<T>` など）が Unity Mono で動作するか確認
- **参照元**:
  - [Unity Issue Tracker: System.Memory missing at runtime](https://issuetracker.unity3d.com/issues/dot-netstandard-2-dot-1-in-the-editor-is-missing-system-dot-memory-system-dot-buffers-at-runtime)
  - [NAudio and .NET Standard](https://markheath.net/post/naudio-netstandard)
- **発見事項**:
  - Unity 2021.2 以降は .NET Standard 2.1 プロファイルを採用しているが、Editor ランタイムで `System.Memory`・`System.Buffers` が `Facades` フォルダに存在しない既知の問題がある
  - 回避策: これらの DLL を `Assets/uLipSync/Plugins/` に配置することで解決する
  - `System.Memory.dll`、`System.Buffers.dll`、`System.Numerics.Vectors.dll`、`System.Runtime.CompilerServices.Unsafe.dll` の 4 ファイルが必要
  - Unity 6000.x では Unity 2021.2 以降の修正が反映されているが、DLL 明示バンドルは安全策として維持する
  - NAudio 2.x のコア部分（`GetAsInterleavedSamples` のシンプルなオーバーロード使用時）は `Span<T>` を使わないパスも持つため、Mono での動作は確認されている
- **影響**:
  - `System.Memory.dll` 等 4 ファイルを必須バンドル対象とする
  - Plugin Importer 設定で Windows x86_64 専用に制限する

### 参考記事の主要な知見（note.com/logic_magic/n/nde2aeb5069e1）

- **コンテキスト**: Unity + NAudio + ASIO の実装例として参照
- **発見事項**:
  - `_asioOut.InitRecordAndPlayback(null, ch, SampleRate)` パターンで録音専用モードを実現
  - `InputChannelOffset` で開始チャンネルを指定する
  - `_asioOut.AudioAvailable += OnAsioOutAudioAvailable` でコールバック登録
  - `e.GetAsInterleavedSamples()` 呼び出しでインターリーブサンプルを取得
  - 記事では `ConcurrentQueue<float>` を使ってコールバック → メインスレッドへデータ転送しているが、**本設計では採用しない**（低レイテンシ要件のため直接コールバックから `OnDataReceived` を呼ぶ）
  - サンプルレートは `InitRecordAndPlayback` の第 3 引数で指定した値が ASIO ドライバに反映される
  - ASIO ドライバのバッファサイズは固定ではなく、ドライバ設定によって変化する（64〜4096 サンプルの範囲）
- **影響**:
  - `ConcurrentQueue` アプローチを排除し、直接コールバック方式を採用する決定を支持
  - バッファは ASIO バッファサイズの変動に対応できる最大サイズ（8192 フレーム × 32 チャンネル）で固定確保する

### NAudio 2.x の DLL 依存グラフ（Unity Mono 向け）

- **コンテキスト**: バンドルすべき DLL の完全な依存関係を把握する
- **発見事項**:
  ```
  NAudio.Asio.dll
    └── NAudio.Core.dll
          ├── System.Memory.dll
          │     ├── System.Runtime.CompilerServices.Unsafe.dll
          │     └── System.Numerics.Vectors.dll
          └── System.Buffers.dll
  ```
  - `NAudio.Asio.dll` は Windows COM / P/Invoke を通じて ASIO ドライバと通信する（ネイティブ DLL 不要、.NET P/Invoke のみ）
  - `NAudio.Core.dll` は Wave フォーマット変換・バッファ管理を担当
  - `System.Memory.dll` は `Span<T>` / `Memory<T>` を提供（Unity Mono では未同梱の場合がある）
- **影響**:
  - 計 6 ファイル（NAudio 2 本 + 依存 4 本）を `Assets/uLipSync/Plugins/Windows/x86_64/` に配置する

---

## アーキテクチャパターン評価

| オプション | 説明 | 強み | リスク・制限 | 採否 |
|-----------|------|------|------------|------|
| 直接コールバック（採用） | ASIO コールバックから直接 `OnDataReceived` を呼び出す | 最低レイテンシ、中間バッファ不要 | コールバックスレッドで Unity API 呼び出し禁止の規律が必要 | 採用 |
| ConcurrentQueue 経由 | コールバック → Queue → Update() でデキュー | スレッド安全性が高い | 1〜2 フレームのレイテンシ追加、GC 圧迫 | 不採用 |
| NativeArray 直接書き込み | ASIO コールバックから NativeArray に直接書き込む | コピー削減 | NativeArray は Unity API（Allocator）が必要、コールバック内使用不可 | 不採用 |

---

## 設計判断

### 判断: OnDataReceived シグネチャの破壊的変更

- **コンテキスト**: ASIO パスではサンプルレートをコールバック引数として受け渡す必要があるが、既存シグネチャ `OnDataReceived(float[], int)` にはサンプルレートがない
- **検討された代替案**:
  1. オーバーロード追加（旧シグネチャを維持）— 既存コードを壊さないが二重管理になる
  2. シグネチャ完全置き換え（採用）— 全呼び出し元を新シグネチャに統一
- **選択されたアプローチ**: `OnDataReceived(float[] samples, int channels, int sampleRate)` のみを公開し、旧シグネチャを削除する
- **根拠**: ASIO と Microphone の両パスで一貫したサンプルレート補正を実現するために必要。上流との merge 分岐は許容済み（仕様に明記）
- **トレードオフ**: 上流 PR との衝突が増えるが、一貫性が高まる
- **フォローアップ**: `OnBakeUpdate(float[], int)` も同様に書き換えが必要（Editor のみのメソッド）

### 判断: 固定プリアロケートバッファ（8192 × 32）

- **コンテキスト**: ASIO コールバック内でのヒープ割り当てを避けつつ、ドライバ依存のバッファサイズ変動に対応する必要がある
- **検討された代替案**:
  1. コールバックごとに `new float[]` — シンプルだが GC 圧迫、RT 非安全
  2. `SamplesPerBuffer` に合わせた動的確保 — コールバック初回のみ確保するが変動に弱い
  3. 最大サイズで固定確保（採用）— 一度だけ確保、変動に対応
- **選択されたアプローチ**: 起動時に `float[8192 * 32]` を確保し、コールバックで `GetAsInterleavedSamples(buffer)` に渡す
- **根拠**: ASIO バッファサイズは最大 4096 サンプル × 最大 32 チャンネルを余裕を持って包含する。一度の確保で安定して動作する
- **トレードオフ**: 約 1 MB の固定メモリ消費（`float` × 8192 × 32 ≈ 1,048,576 bytes = 1 MB）

### 判断: IAudioInputSource インターフェースの導入

- **コンテキスト**: `uLipSyncMicrophone` と `uLipSyncAsioInput` の両クラスをテストで差し替え可能にする必要がある
- **検討された代替案**:
  1. 抽象基底クラス — MonoBehaviour 継承との多重継承不可の制約がある
  2. インターフェース（採用）— MonoBehaviour とともに実装可能
- **選択されたアプローチ**: `IAudioInputSource` インターフェースを定義し、両クラスに実装させる
- **根拠**: EditMode テストで実機 ASIO ハードウェアなしにロック契約・パイプライン統合を検証できる

### 判断: _lockObject の再利用（新ロック機構不採用）

- **コンテキスト**: ASIO コールバックスレッドと Unity メインスレッド（ScheduleJob）の排他制御
- **検討された代替案**:
  1. 新しい lock オブジェクトを `uLipSyncAsioInput` に導入する
  2. `uLipSync` が既存で持つ `_lockObject` をそのまま使用する（採用）
- **選択されたアプローチ**: `uLipSync._lockObject` を流用する（`OnDataReceived` 内でロック取得済み）
- **根拠**: `OnDataReceived` は既に内部で `lock(_lockObject)` しているため、新たなロック機構を導入しても意味がない。二重ロックはデッドロックリスクを生む

---

## リスクと緩和策

| リスク | 緩和策 |
|-------|-------|
| 上流リポジトリ（hecomi/uLipSync）との merge 分岐 | `OnDataReceived` 変更は局所的。PR マージ時の差分は明確に文書化。フォーク独自の commit として管理 |
| NAudio 2.x + Mono での `System.Memory` / `Span<T>` 互換性問題 | 4 つの依存 DLL を明示的にバンドル。`GetAsInterleavedSamples(float[])` の float[] オーバーロード（Span 非依存）を優先使用 |
| ASIO バッファサイズがセッション中に変化する | 8192 フレーム × 32 チャンネルの固定バッファは ASIO 最大仕様の 2 倍の余裕を持つ。`SamplesPerBuffer × InputBuffers.Length` が超過した場合は `Interlocked` エラーフラグを立て安全停止 |
| `uLipSyncMicrophone` と `uLipSyncAsioInput` の同時実行 | 両コンポーネントが同じ `uLipSync` を参照している場合、`_lockObject` の競合が増加する。同一シーンでの同時使用は非推奨とし、README に明記 |
| 非 Windows Editor でのコンパイルエラー | `#if UNITY_STANDALONE_WIN || UNITY_EDITOR` ガードで囲む。ただし `AsioOut.GetDriverNames()` が非 Windows Editor で例外を投げる場合は `try-catch` で空配列を返す |
| `AsioOut.Dispose()` の未呼び出しによるドライバロック | `try-finally` パターンで `Dispose()` を保証。`OnDisable`・`OnDestroy` でも自動呼び出し |

---

## 参考文献

- [NAudio AsioRecording.md（公式）](https://github.com/naudio/NAudio/blob/master/Docs/AsioRecording.md) — AsioOut 録音 API の基本パターン
- [NAudio AsioOut.cs ソースコード](https://github.com/naudio/NAudio/blob/master/NAudio.Asio/AsioOut.cs) — API シグネチャの確認
- [ASIO Recording in NAudio（Mark Heath ブログ）](https://markheath.net/post/asio-recording-in-naudio) — `GetAsInterleavedSamples` の使用例
- [NAudio and .NET Standard](https://markheath.net/post/naudio-netstandard) — .NET Standard 対応の経緯
- [Unity Issue Tracker: System.Memory missing](https://issuetracker.unity3d.com/issues/dot-netstandard-2-dot-1-in-the-editor-is-missing-system-dot-memory-system-dot-buffers-at-runtime) — Unity Mono での依存 DLL 問題
- [参考記事（note.com）](https://note.com/logic_magic/n/nde2aeb5069e1) — Unity + NAudio + ASIO の実装例（ConcurrentQueue アプローチ）
