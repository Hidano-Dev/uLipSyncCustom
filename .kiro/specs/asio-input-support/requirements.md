# 要件ドキュメント

## プロジェクト概要

uLipSync（hecomi/uLipSync の fork）に ASIO オーディオ入力サポートを追加する。  
対象ユーザーは主に Windows 上で活動する VTuber 向けで、既存の `UnityEngine.Microphone` API よりも低レイテンシかつ高品質なオーディオキャプチャを必要としている。  
実装アプローチは NAudio ライブラリ（`AsioOut` クラス）を用いたネイティブ ASIO ドライバ連携とし、取得した PCM float サンプルを既存の `uLipSync.OnDataReceived` パイプラインに供給する。  
参考記事: https://note.com/logic_magic/n/nde2aeb5069e1

## スコープ境界

- **対象スコープ**: ASIO デバイスの列挙・選択、録音開始/停止、サンプルデータのパイプライン供給、エラーハンドリング、プラットフォームガード、エディタ対応、後方互換性
- **対象外**: ASIO ドライバ自体のインストール補助、ASIO コントロールパネルの UI 操作、MIDI/タイムコード処理、macOS / Linux / WebGL 対応
- **隣接仕様との関係**: 既存の `uLipSyncMicrophone`（`UnityEngine.Microphone` 経由）は本機能と独立して動作し続けなければならない

---

## 要件

### 要件 1: ASIO デバイス列挙

**目的:** VTuber ユーザーとして、システムにインストールされた ASIO デバイスの一覧を取得できるようにしたい。それによって使用するデバイスをインスペクタから選択できる。

#### 受け入れ基準

1. Where `UNITY_STANDALONE_WIN` プラットフォームが対象, the uLipSyncAsioInput shall NAudio の `AsioOut.GetDriverNames()` を呼び出してシステム上の ASIO ドライバ名の配列を返す。
2. When ASIO デバイスが 1 件以上列挙された, the uLipSyncAsioInput shall 先頭デバイスをデフォルト選択としてインスペクタに表示する。
3. If ASIO ドライバが 1 件も検出されない, then the uLipSyncAsioInput shall 空のリストを返し、インスペクタに「ASIO ドライバが見つかりません」と警告を表示する。
4. The uLipSyncAsioInput shall ASIO デバイス列挙ロジック全体を `#if UNITY_STANDALONE_WIN || UNITY_EDITOR` プリプロセッサガードで囲む。
5. When Unity Editor がエディタモードで実行中, the uLipSyncAsioInput shall Windows 以外のホスト OS であっても ASIO デバイス列挙を試み、失敗した場合はエラーではなく空リストを返す。

---

### 要件 2: ASIO 録音の開始と停止

**目的:** VTuber ユーザーとして、選択した ASIO デバイスの録音を実行時に開始・停止できるようにしたい。それによって必要なタイミングにのみ低レイテンシキャプチャを行える。

#### 受け入れ基準

1. When `StartRecord()` が呼び出された, the uLipSyncAsioInput shall 選択中の ASIO ドライバ名で `AsioOut` を初期化し、録音を開始する。
2. When `StopRecord()` が呼び出された, the uLipSyncAsioInput shall `AsioOut.Stop()` および `AsioOut.Dispose()` を呼び出してリソースを解放する。
3. While 録音が実行中, the uLipSyncAsioInput shall `isRecording` プロパティを `true` に保持する。
4. When コンポーネントが `OnDisable` または `OnDestroy` で無効化された, the uLipSyncAsioInput shall 録音中であれば自動的に `StopRecord()` を呼び出す。
5. When `isAutoStart` が `true` かつコンポーネントが `OnEnable` で有効化された, the uLipSyncAsioInput shall 有効な ASIO デバイスが選択されている場合に自動的に録音を開始する。
6. If 録音開始時に `AsioOut` の初期化が失敗した, then the uLipSyncAsioInput shall `Debug.LogError` でエラーメッセージを出力し、`isRecording` を `false` のまま維持する。

---

### 要件 3: サンプルレート・バッファ・チャンネル設定

**目的:** VTuber ユーザーとして、ASIO デバイスのサンプルレートや入力チャンネルを設定できるようにしたい。それによって機材に合わせた最適な設定でキャプチャできる。

#### 受け入れ基準

1. The uLipSyncAsioInput shall インスペクタに `inputChannelOffset`（整数、デフォルト 0）および `inputChannelCount`（整数、デフォルト 1）プロパティを公開する。
2. When ASIO バッファコールバックが発火した, the uLipSyncAsioInput shall `e.GetAsInterleavedSamples(buffer)` で interleaved float 配列を取得し、`inputChannelOffset` から `inputChannelCount` チャンネル分のサンプルを抽出する。
3. The uLipSyncAsioInput shall バッファサイズを ASIO ドライバが決定するものとし、ユーザーが Unity 側で直接設定できないことを許容する（ドライバのコントロールパネルで設定するものとする）。
4. When ASIO ドライバのサンプルレートと Unity の `AudioSettings.outputSampleRate` が異なる, the uLipSyncAsioInput shall サンプルレート差分を `uLipSync` 解析ジョブの `outputSampleRate` パラメータで補正できるよう、ASIO 側のサンプルレートをプロパティとして公開する。
5. If 指定した `inputChannelOffset + inputChannelCount` が ASIO デバイスの最大入力チャンネル数を超える, then the uLipSyncAsioInput shall `Debug.LogWarning` で警告を出力し、利用可能な範囲内に値をクランプする。

---

### 要件 4: uLipSync 解析パイプラインとの統合

**目的:** VTuber ユーザーとして、ASIO から取得した音声データが既存の MFCC 解析パイプラインに供給されるようにしたい。それによって既存のリップシンク機能をそのまま活用できる。

#### 受け入れ基準

1. While 録音が実行中, the uLipSyncAsioInput shall ASIO バッファコールバックのたびに `uLipSync.OnDataReceived(float[] samples, int channels)` を呼び出してサンプルデータを供給する。
2. The uLipSyncAsioInput shall `uLipSync` コンポーネントへの参照をインスペクタの `lipSync` フィールドで受け取る（`[SerializeField]`）。
3. When ASIO コールバックがオーディオスレッドから呼ばれた, the uLipSyncAsioInput shall `ConcurrentQueue<float[]>` 等のスレッドセーフな手段でサンプルを蓄積し、Unity メインスレッドまたは `OnDataReceived` のロック機構と安全に連携する。
4. The uLipSyncAsioInput shall `uLipSyncMicrophone` と同一の `uLipSync` コンポーネントに接続可能なものとし、いずれか一方のみがアクティブである場合に正常動作する。
5. When `lipSync` フィールドが null のとき, the uLipSyncAsioInput shall データ供給をスキップし、`Debug.LogWarning` で通知する。

---

### 要件 5: エラーハンドリングと例外安全性

**目的:** VTuber ユーザーとして、ASIO ドライバが未インストールまたはデバイスが使用中であっても Unity アプリがクラッシュしないようにしたい。

#### 受け入れ基準

1. If ASIO ドライバ初期化時に `System.Exception` がスローされた, then the uLipSyncAsioInput shall 例外をキャッチし `Debug.LogError` でメッセージを出力したうえで録音を開始しない。
2. If ASIO デバイスが別プロセスによって占有中（デバイスビジー）の場合, then the uLipSyncAsioInput shall 例外をキャッチし、ユーザーに「デバイスが使用中です。他のアプリを終了してから再試行してください」という趣旨のエラーメッセージを `Debug.LogError` で出力する。
3. If 録音中に ASIO コールバックで例外が発生した, then the uLipSyncAsioInput shall 例外をキャッチしてログに記録し、録音を安全に停止する。
4. The uLipSyncAsioInput shall `AsioOut` の生存期間を管理し、`Dispose` を必ず呼び出すことで非管理リソースリークを防止する。
5. If 録音停止後に再度 `StartRecord()` が呼ばれた, then the uLipSyncAsioInput shall 前回の `AsioOut` インスタンスが破棄済みであることを確認してから新しいインスタンスを生成する。

---

### 要件 6: プラットフォームガードとエディタ対応

**目的:** 開発者として、ASIO 関連コードが Windows Standalone ビルド以外でコンパイルエラーや実行時エラーを引き起こさないようにしたい。

#### 受け入れ基準

1. The uLipSyncAsioInput shall ASIO 依存コード全体を `#if UNITY_STANDALONE_WIN || UNITY_EDITOR` で囲み、それ以外のプラットフォームでは空実装となる。
2. Where `UNITY_STANDALONE_WIN` ではないプラットフォーム（Android、iOS、WebGL 等）でビルドした場合, the uLipSyncAsioInput shall コンパイルエラーを発生させない。
3. When Unity Editor が Windows 以外の OS（macOS / Linux）で実行中, the uLipSyncAsioInput shall ASIO 列挙を試みて失敗した場合に `Debug.LogWarning` を出力し、クラッシュしない。
4. The uLipSyncAsioInput shall NAudio DLL（`NAudio.dll` 等）を `Assets/uLipSync/Plugins/` 以下に配置し、プラットフォームを `Windows (x86_64)` に限定したプラグイン設定を適用する。
5. When Unity Editor のインスペクタでコンポーネントを表示した場合, the uLipSyncAsioInput shall Windows Editor 以外でも null 参照例外を発生させずにインスペクタを描画する。

---

### 要件 7: 後方互換性（既存 UnityEngine.Microphone パスの維持）

**目的:** 既存ユーザーとして、ASIO を選択しない場合に `uLipSyncMicrophone` の既存動作が変わらないようにしたい。

#### 受け入れ基準

1. The uLipSyncAsioInput shall `uLipSyncMicrophone` クラスに一切の変更を加えないか、後方互換性を壊す変更を行わない。
2. When シーンに `uLipSyncMicrophone` と `uLipSyncAsioInput` が同時に存在する場合, the uLipSync shall どちらか一方のみが同時に `OnDataReceived` を呼び出せるよう排他制御の設計を推奨ドキュメントに明記する。
3. The uLipSync shall `uLipSyncAsioInput` を追加しても、既存の `AudioSource` + `OnAudioFilterRead` 経由のデータ供給パスが引き続き機能する。
4. When `uLipSyncAsioInput` がシーンに存在しない, the uLipSync shall `uLipSyncMicrophone` または `AudioSource` パスのみで完全に動作する。
5. The uLipSyncAsioInput shall `com.hecomi.ulipsync` パッケージのバージョンを変更せず、既存の `package.json` に追記する形で NAudio 依存関係を管理する。

---

### 要件 8: エディタ UI とデバッグ情報

**目的:** 開発者として、インスペクタ上で ASIO の状態を確認し、問題を素早く診断できるようにしたい。

#### 受け入れ基準

1. The uLipSyncAsioInput shall インスペクタに現在の `isRecording` 状態、選択デバイス名、検出されたバッファサイズ（最後に受信したコールバックのサンプル数）を読み取り専用フィールドとして表示する。
2. When インスペクタの「デバイス更新」ボタンが押された, the uLipSyncAsioInput shall `AsioOut.GetDriverNames()` を再実行してデバイスリストを更新する。
3. The uLipSyncAsioInput shall `CustomEditor` を実装し、ASIO が利用不可なプラットフォームでは「このコンポーネントは Windows Standalone でのみ動作します」という趣旨のヘルプボックスを表示する。
4. When `isAutoStart` が `false` のとき, the uLipSyncAsioInput shall インスペクタに「Start Recording」および「Stop Recording」ボタンを表示し、エディタ実行中でも手動で録音を制御できるようにする。
