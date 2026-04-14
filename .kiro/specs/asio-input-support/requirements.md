# 要件ドキュメント

## プロジェクト概要

本仕様は hecomi/uLipSync（Unity MFCC ベースのリップシンク UPM パッケージ、`com.hecomi.ulipsync` v3.1.5）のフォークに対して ASIO オーディオ入力サポートを追加するものである。  
対象ユーザーは主に Windows 上で活動する VTuber であり、既存の `UnityEngine.Microphone` API よりも低レイテンシかつ高品質なオーディオキャプチャを必要としている。  
実装アプローチは NAudio 2.x（`NAudio.Core.dll` + `NAudio.Asio.dll`）を用いたネイティブ ASIO ドライバ連携とし、取得した PCM float サンプルを新しいシグネチャ `OnDataReceived(float[] samples, int channels, int sampleRate)` 経由で既存の uLipSync 解析パイプラインに供給する。  
参考記事: https://note.com/logic_magic/n/nde2aeb5069e1  
作業ブランチ: `feature/hidano/asio`

## スコープ境界

- **対象スコープ**: ASIO デバイスの列挙・選択、録音開始/停止、サンプルデータのパイプライン供給、サンプルレート伝播、リアルタイム安全バッファ管理、エラーハンドリング、プラットフォームガード、NAudio プラグイン配置、`IAudioInputSource` 抽象、`OnDataReceived` シグネチャ変更と全呼び出し元の書き換え、エディタ UI、検証戦略
- **対象外**: ASIO ドライバ自体のインストール補助、ASIO コントロールパネルの UI 操作、MIDI / タイムコード処理、macOS / Linux / WebGL 対応、IL2CPP スクリプティングバックエンドへの対応
- **隣接仕様との関係**: 既存の `uLipSyncMicrophone`（`UnityEngine.Microphone` 経由）は本機能と独立して動作し続けなければならない。`uLipSyncBakedDataPlayer` および `uLipSyncCalibrationAudioPlayer` は `OnDataReceived` 新シグネチャへの書き換えが必要であり、これらも本仕様のスコープに含む

## 前提条件・仮定

- **Unity バージョン**: Unity 6000.3.10f1 のみを対象とする
- **スクリプティングバックエンド**: Mono のみ対応。IL2CPP は明示的に非対応とし、README および本要件ドキュメントにその旨を明記する
- **NAudio バージョン**: NAudio 2.x（`NAudio.Core.dll`、`NAudio.Asio.dll`）を使用する
- **依存 DLL**: `System.Memory.dll`、`System.Buffers.dll`、`System.Numerics.Vectors.dll`、`System.Runtime.CompilerServices.Unsafe.dll` を必要に応じて同梱する
- **ライセンス義務**: NAudio は MIT ライセンスであり、ライセンス通知ファイル（`NAudio-LICENSE.txt`）を `Assets/uLipSync/Plugins/` に必ず同梱する
- **プラットフォーム**: Windows x86_64 Standalone のみを実行対象とする。Editor 上では Windows / 非 Windows ともに UI を表示するが、非 Windows では ASIO 機能は動作しない
- **破壊的変更**: `OnDataReceived(float[], int)` の削除は破壊的変更であり、上流リポジトリとの merge 分岐を許容する

---

## 要件

### 要件 1: ASIO デバイス列挙

**目的:** VTuber ユーザーとして、システムにインストールされた ASIO デバイスの一覧を取得できるようにしたい。それによって使用するデバイスをインスペクタから選択できる。

#### 受け入れ基準

1. Where `UNITY_STANDALONE_WIN || UNITY_EDITOR` プラットフォームガードが有効, the uLipSyncAsioInput shall NAudio の `AsioOut.GetDriverNames()` を呼び出してシステム上の ASIO ドライバ名の配列を返す。
2. When ASIO デバイスが 1 件以上列挙された, the uLipSyncAsioInput shall 先頭デバイスをデフォルト選択としてインスペクタに表示する。
3. If ASIO ドライバが 1 件も検出されない, then the uLipSyncAsioInput shall 空のリストを返し、インスペクタに「ASIO ドライバが見つかりません」という趣旨の警告を表示する。
4. The uLipSyncAsioInput shall ASIO デバイス列挙ロジック全体を `#if UNITY_STANDALONE_WIN || UNITY_EDITOR` プリプロセッサガードで囲む。
5. If Unity Editor が Windows 以外の OS で実行中かつ `AsioOut.GetDriverNames()` が例外をスローした, then the uLipSyncAsioInput shall 例外をキャッチして空リストを返し、クラッシュしない。

---

### 要件 2: 録音ライフサイクル（開始・停止・自動起動・OnDisable）

**目的:** VTuber ユーザーとして、選択した ASIO デバイスの録音を実行時に開始・停止でき、コンポーネントの有効・無効に連動して自動的に制御されるようにしたい。それによって必要なタイミングにのみ低レイテンシキャプチャを行える。

#### 受け入れ基準

1. When `StartRecord()` が呼び出された, the uLipSyncAsioInput shall 選択中の ASIO ドライバ名で `AsioOut` を初期化し、録音を開始する。
2. When `StopRecord()` が呼び出された, the uLipSyncAsioInput shall `AsioOut.Stop()` および `AsioOut.Dispose()` を呼び出してリソースを解放し、`isRecording` を `false` にセットする。
3. While 録音が実行中, the uLipSyncAsioInput shall `isRecording` プロパティを `true` に保持する。
4. When コンポーネントが `OnDisable` または `OnDestroy` で無効化された, the uLipSyncAsioInput shall 録音中であれば自動的に `StopRecord()` を呼び出す。
5. When `isAutoStart` が `true` かつコンポーネントが `OnEnable` で有効化された, the uLipSyncAsioInput shall 有効な ASIO デバイスが選択されている場合に自動的に録音を開始する。
6. If 録音開始時に `AsioOut` の初期化が失敗した, then the uLipSyncAsioInput shall `Debug.LogError` でエラーメッセージを出力し、`isRecording` を `false` のまま維持する。
7. If 録音停止後に再度 `StartRecord()` が呼ばれた, then the uLipSyncAsioInput shall 前回の `AsioOut` インスタンスが破棄済みであることを確認してから新しいインスタンスを生成する。

---

### 要件 3: サンプルフォーマット（チャンネル、固定プリアロケートバッファ、サンプルレート伝播）

**目的:** VTuber ユーザーとして、ASIO デバイスの入力チャンネルを設定し、キャプチャしたサンプルのサンプルレートが解析ジョブに正確に伝わるようにしたい。それによって機材に合わせた最適な設定でリップシンク解析が行える。

#### 受け入れ基準

1. The uLipSyncAsioInput shall インスペクタに `inputChannelOffset`（整数、デフォルト 0）および `inputChannelCount`（整数、デフォルト 1）プロパティを公開する。
2. The uLipSyncAsioInput shall 起動時に 8192 フレーム × 32 チャンネルのサイズで再利用可能なサンプルバッファを事前確保（プリアロケート）し、ASIO コールバック中に新たなヒープ割り当てを行わない。
3. When ASIO バッファコールバックが発火した, the uLipSyncAsioInput shall プリアロケートバッファに `e.GetAsInterleavedSamples()` でサンプルを書き込み、`inputChannelOffset` から `inputChannelCount` チャンネル分のサンプルを抽出する。
4. When ASIO バッファコールバックが発火した, the uLipSyncAsioInput shall ASIO ドライバから取得したサンプルレートを `OnDataReceived(float[] samples, int channels, int sampleRate)` の `sampleRate` 引数として渡す。
5. The uLipSync shall `ScheduleJob()` において `OnDataReceived` から渡された `sampleRate` パラメータを用いてサンプルレートを補正する（`AudioSettings.outputSampleRate` ではなくコールバック由来の値を使用する）。
6. If 指定した `inputChannelOffset + inputChannelCount` が ASIO デバイスの最大入力チャンネル数を超える, then the uLipSyncAsioInput shall `Debug.LogWarning` で警告を出力し、利用可能な範囲内に値をクランプする。

---

### 要件 4: パイプライン統合（直接コールバック、RT安全規則、Interlocked エラーフラグ）

**目的:** VTuber ユーザーとして、ASIO から取得した音声データが既存の MFCC 解析パイプラインに安全かつ直接供給されるようにしたい。それによって低レイテンシを維持したままリップシンク機能を活用できる。

#### 受け入れ基準

1. While 録音が実行中, the uLipSyncAsioInput shall ASIO バッファコールバックから直接 `uLipSync.OnDataReceived(float[] samples, int channels, int sampleRate)` を呼び出してサンプルデータを供給する（ConcurrentQueue 等の中間バッファは使用しない）。
2. The uLipSyncAsioInput shall スレッド同期に `uLipSync` が既存で保持する `_lockObject` をそのまま使用し、新たなロック機構を導入しない。
3. The uLipSyncAsioInput shall ASIO コールバック内で Unity API（`Debug.Log` 等）を呼び出さない。
4. If ASIO コールバック内でエラー状態が発生した, then the uLipSyncAsioInput shall `Interlocked.Exchange` を用いてエラー状態フィールドを更新し、実際のログ出力は次の `Update()` 呼び出し時に行う。
5. The uLipSyncAsioInput shall `uLipSync` コンポーネントへの参照をインスペクタの `lipSync` フィールド（`[SerializeField]`）で受け取る。
6. If `lipSync` フィールドが null のとき, then the uLipSyncAsioInput shall データ供給をスキップし、`Update()` サイクルで `Debug.LogWarning` を通知する（コールバック内では通知しない）。

---

### 要件 5: エラーハンドリング（ドライバ不在、デバイスビジー、コールバック例外、Dispose 保証）

**目的:** VTuber ユーザーとして、ASIO ドライバが未インストールまたはデバイスが使用中であっても Unity アプリがクラッシュせず、明確なエラーメッセージを得られるようにしたい。

#### 受け入れ基準

1. If ASIO ドライバ初期化時に `System.Exception` がスローされた, then the uLipSyncAsioInput shall 例外をキャッチし `Debug.LogError` でメッセージを出力したうえで録音を開始しない。
2. If ASIO デバイスが別プロセスによって占有中（デバイスビジー）の場合, then the uLipSyncAsioInput shall 例外をキャッチし、「デバイスが使用中です。他のアプリを終了してから再試行してください」という趣旨のエラーメッセージを `Debug.LogError` で出力する。
3. If ASIO コールバック内で例外が発生した, then the uLipSyncAsioInput shall 例外をキャッチし、`Interlocked.Exchange` でエラー状態を記録し、次の `Update()` でログ出力のうえ録音を安全に停止する。
4. The uLipSyncAsioInput shall `AsioOut` の生存期間を管理し、正常終了・例外発生いずれの場合も `Dispose` を必ず呼び出すことで非管理リソースリークを防止する。

---

### 要件 6: プラットフォームガードおよびプラグインインポート設定（NAudio 2.x + 依存 DLL）

**目的:** 開発者として、NAudio 関連の DLL が Windows x86_64 環境にのみ適用され、他プラットフォームのビルドに影響を与えないようにしたい。また MIT ライセンス義務を確実に履行したい。

#### 受け入れ基準

1. The uLipSyncAsioInput shall ASIO 依存コード全体を `#if UNITY_STANDALONE_WIN || UNITY_EDITOR` で囲み、それ以外のプラットフォームでは空実装となる。
2. Where `UNITY_STANDALONE_WIN` ではないプラットフォーム（Android、iOS、WebGL 等）でビルドした場合, the uLipSyncAsioInput shall コンパイルエラーを発生させない。
3. The uLipSyncAsioInput shall `NAudio.Core.dll`、`NAudio.Asio.dll`、`System.Memory.dll`、`System.Buffers.dll`、`System.Numerics.Vectors.dll`、`System.Runtime.CompilerServices.Unsafe.dll` を `Assets/uLipSync/Plugins/` 以下に配置する。
4. The uLipSyncAsioInput shall 上記 DLL のプラグインインポート設定でプラットフォームを Windows x86_64 のみに限定する。
5. The uLipSyncAsioInput shall NAudio の MIT ライセンス通知ファイル（`NAudio-LICENSE.txt`）を `Assets/uLipSync/Plugins/` に同梱する。
6. The uLipSyncAsioInput shall Unity 6000.3.10f1 および Mono スクリプティングバックエンドのみをサポート対象とし、IL2CPP は非対応である旨を README に明記する。

---

### 要件 7: 破壊的 API 変更 — OnDataReceived シグネチャ置き換えと全呼び出し元の書き換え

**目的:** 開発者として、サンプルレートをコールバック経由で正確に伝達するために `OnDataReceived` のシグネチャを更新し、パッケージ内の全呼び出し元を新シグネチャに統一したい。それによって ASIO と Microphone の両パスで一貫したサンプルレート補正が行える。

#### 受け入れ基準

1. The uLipSync shall 既存の `OnDataReceived(float[] samples, int channels)` を削除し、`OnDataReceived(float[] samples, int channels, int sampleRate)` のみを公開インターフェースとして提供する。
2. The uLipSync shall `OnAudioFilterRead` 内の `OnDataReceived` 呼び出しを新シグネチャに書き換え、`sampleRate` 引数に `AudioSettings.outputSampleRate` を渡す。
3. The uLipSyncBakedDataPlayer shall `OnDataReceived` の呼び出しを新シグネチャに書き換える。
4. The uLipSyncCalibrationAudioPlayer shall `OnDataReceived` の呼び出しを新シグネチャに書き換える。
5. The uLipSync shall `ScheduleJob()` において `OnDataReceived` から渡された `sampleRate` を使用してサンプルレートを設定し、旧来の `AudioSettings.outputSampleRate` 固定参照を廃止する。
6. The uLipSyncAsioInput shall 新シグネチャ `OnDataReceived(float[] samples, int channels, int sampleRate)` を用いて ASIO コールバックからサンプルデータを供給する。

---

### 要件 8: IAudioInputSource 抽象

**目的:** 開発者として、`uLipSyncMicrophone` と `uLipSyncAsioInput` が共通インターフェースを持つようにしたい。それによって EditMode テストでモックを用いた検証が可能になり、実機 ASIO ハードウェアなしにバッファ処理・ロック契約・パイプライン統合を確認できる。

#### 受け入れ基準

1. The uLipSyncAsioInput shall `IAudioInputSource` インターフェースを新たに定義し、`uLipSyncMicrophone` および `uLipSyncAsioInput` の両クラスにそのインターフェースを実装させる。
2. The uLipSyncAsioInput shall `IAudioInputSource` が少なくとも `StartRecord()`、`StopRecord()`、`isRecording` を含む共通サーフェスを公開するよう定義する。
3. Where `IAudioInputSource` のモック実装が存在する, the uLipSync shall 実機ハードウェアなしにバッファ処理・`_lockObject` ロック契約・パイプライン統合を EditMode テストで検証できる。
4. The uLipSyncAsioInput shall `IAudioInputSource` の導入が `uLipSyncMicrophone` の既存の公開 API を破壊しないよう実装する。

---

### 要件 9: エディタ UI（デバイスリスト更新、ステータス表示、Start/Stop ボタン、非 Windows ヘルプボックス）

**目的:** 開発者として、インスペクタ上で ASIO の状態を確認し、問題を素早く診断できるようにしたい。それによってハードウェア設定の問題を迅速に特定できる。

#### 受け入れ基準

1. The uLipSyncAsioInput shall インスペクタに現在の `isRecording` 状態、選択デバイス名、最後に受信したコールバックのサンプル数（バッファサイズ）を読み取り専用フィールドとして表示する。
2. When インスペクタの「デバイス更新」ボタンが押された, the uLipSyncAsioInput shall `AsioOut.GetDriverNames()` を再実行してデバイスリストを更新する。
3. The uLipSyncAsioInput shall `CustomEditor` を実装し、ASIO が利用不可なプラットフォームでは「このコンポーネントは Windows Standalone / Mono 環境でのみ動作します」という趣旨のヘルプボックスを表示する。
4. When `isAutoStart` が `false` かつ Unity Editor が再生中, the uLipSyncAsioInput shall インスペクタに「Start Recording」および「Stop Recording」ボタンを表示し、手動で録音を制御できるようにする。
5. When Unity Editor のインスペクタでコンポーネントを表示した場合, the uLipSyncAsioInput shall Windows 以外の OS であっても null 参照例外を発生させずにインスペクタを描画する。

---

### 要件 10: 検証戦略（手動テストプランおよび IAudioInputSource モックによる EditMode テスト）

**目的:** 開発者として、実機 ASIO ハードウェアなしに自動テストを実行でき、実機検証の手順が明確であるようにしたい。それによって CI 環境および開発者ローカル環境の両方で品質を確保できる。

#### 受け入れ基準

1. The uLipSyncAsioInput shall `.kiro/specs/asio-input-support/test-plan.md` に手動テストプランを配置し、ASIO デバイスを用いた実機検証手順（デバイス列挙、録音開始/停止、リップシンク動作確認、エラーケース）を記載する。
2. The uLipSyncAsioInput shall `IAudioInputSource` のモック実装を用いた EditMode テストを提供し、実機 ASIO ハードウェアなしにバッファ処理・`_lockObject` ロック契約・パイプライン統合を検証できるようにする。
3. The uLipSyncAsioInput shall EditMode テストにおいて `OnDataReceived` 新シグネチャへの呼び出しが正しく行われること（`samples`、`channels`、`sampleRate` の各引数）を検証する。
4. The uLipSyncAsioInput shall EditMode テストにおいて ASIO コールバック内の Unity API 呼び出し禁止ルール（要件 4-3）が遵守されていることをモックで検証できる構造とする。
5. Where 実機 ASIO ハードウェアが利用可能な環境, the uLipSyncAsioInput shall test-plan.md の手順に従い、リップシンク動作・サンプルレート補正・エラーケースを手動で確認する。
