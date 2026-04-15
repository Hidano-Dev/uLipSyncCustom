# 実装計画

<!-- 
注記: 本タスクリストは `-y` フラグ付き実行により生成されました。
要件および設計のレビューは validate-design の GO 判定と組み合わせた暗黙的承認として扱い、
approvals.requirements.approved および approvals.design.approved を true に設定しています。
-->

---

## タスク一覧

- [ ] 1. NAudio プラグイン DLL の配置とプラグインインポート設定
- [ ] 1.1 NAudio 2.x および依存 DLL を Plugins ディレクトリに配置する
  - `Assets/uLipSync/Plugins/Windows/x86_64/` ディレクトリを作成し、`NAudio.Core.dll`、`NAudio.Asio.dll`、`System.Memory.dll`、`System.Buffers.dll`、`System.Numerics.Vectors.dll`、`System.Runtime.CompilerServices.Unsafe.dll` の 6 本を配置する
  - 各 DLL の Unity Plugin Importer 設定でプラットフォームを Windows x86_64 Standalone のみに限定し、他プラットフォームへの適用を無効にする
  - `NAudio-LICENSE.txt` を `Assets/uLipSync/Plugins/` に配置して MIT ライセンス通知義務を履行する
  - Unity エディタを再起動したとき、コンソールにプラグインロードエラーが発生しないことを確認できる
  - _Requirements: 6.3, 6.4, 6.5_

- [ ] 1.2 asmdef の precompiledReferences に NAudio DLL 参照を追加する
  - `Assets/uLipSync/Runtime/uLipSync.Runtime.asmdef` の `precompiledReferences` 配列に `NAudio.Core.dll` および `NAudio.Asio.dll` を追加する
  - 変更後、Unity が asmdef を再コンパイルしてエラーなしにビルドが通ることを確認できる
  - _Requirements: 6.3, 6.4_

---

- [ ] 2. 破壊的 API 変更 — OnDataReceived シグネチャ置き換えと全呼び出し元の書き換え
- [ ] 2.1 Common.cs の AudioFilterReadEvent 型を新シグネチャに変更する
  - `Assets/uLipSync/Runtime/Core/Common.cs` で `AudioFilterReadEvent` の型を `UnityEvent<float[], int>` から `UnityEvent<float[], int, int>` に変更する（CI-1 Option A）
  - ビルド後、`AudioFilterReadEvent` を使用している既存 MonoBehaviour がコンパイルエラーを出さないことを確認できる（後続タスクで呼び出し元を更新するため、この時点ではエラーが出ることを認識した上で進める）
  - _Requirements: 7.1, 7.2_
  - _Boundary: Common.cs_

- [ ] 2.2 uLipSync.cs の OnDataReceived シグネチャを変更し ScheduleJob のサンプルレート補正を実装する
  - `Assets/uLipSync/Runtime/uLipSync.cs` で既存の `OnDataReceived(float[] samples, int channels)` を削除し、`OnDataReceived(float[] samples, int channels, int sampleRate)` のみを公開する
  - `OnDataReceived` 内で受け取った `sampleRate` を `_cachedSampleRate` フィールドに保存し（既存の `_lockObject` で保護）、`ScheduleJob()` で `AudioSettings.outputSampleRate` の固定参照を `_cachedSampleRate` に置き換える
  - `OnAudioFilterRead` 内の `OnDataReceived` 呼び出しを新シグネチャに書き換え、第 3 引数に `AudioSettings.outputSampleRate` を渡す
  - 変更後、`uLipSync` クラスが単体でコンパイルエラーなしにビルドできる
  - _Requirements: 7.1, 7.2, 7.5, 3.5_
  - _Boundary: uLipSync.cs_

- [ ] 2.3 uLipSyncAudioSource.cs の onAudioFilterRead.Invoke 呼び出しに sampleRate を追加する
  - `Assets/uLipSync/Runtime/uLipSyncAudioSource.cs` で `onAudioFilterRead.Invoke(data, channels)` を `onAudioFilterRead.Invoke(data, channels, AudioSettings.outputSampleRate)` に書き換える
  - 変更後、AudioSource 経由の lipsync がコンパイルエラーなしにビルドできる
  - _Requirements: 7.2_
  - _Boundary: uLipSyncAudioSource.cs_

- [ ] 2.4 (P) uLipSyncBakedDataPlayer.cs の OnDataReceived 呼び出しを新シグネチャに書き換える
  - `Assets/uLipSync/Runtime/uLipSyncBakedDataPlayer.cs` の `OnBakeUpdate` 内の `OnDataReceived(float[], int)` 呼び出しを `OnDataReceived(float[], int, AudioSettings.outputSampleRate)` に書き換える
  - 変更後、BakedDataPlayer を含むシーンがコンパイルエラーなしにビルドできる
  - _Requirements: 7.3_
  - _Boundary: uLipSyncBakedDataPlayer.cs_
  - _Depends: 2.2_

- [ ] 2.5 (P) uLipSyncCalibrationAudioPlayer.cs の OnDataReceived 呼び出しを新シグネチャに書き換える
  - `Assets/uLipSync/Runtime/uLipSyncCalibrationAudioPlayer.cs` で `OnDataReceived` を直接呼び出している箇所（`OnAudioRead` コールバック経由）を新シグネチャ `(float[], int, int)` に書き換える
  - 変更後、CalibrationAudioPlayer を含むシーンがコンパイルエラーなしにビルドできる
  - _Requirements: 7.4_
  - _Boundary: uLipSyncCalibrationAudioPlayer.cs_
  - _Depends: 2.2_

---

- [ ] 3. IAudioInputSource インターフェース定義
- [ ] 3.1 IAudioInputSource インターフェースを新規ファイルに定義する
  - `Assets/uLipSync/Runtime/IAudioInputSource.cs` を新規作成し、`StartRecord()`、`StopRecord()`、`bool isRecording` を含む共通サーフェスを定義する
  - インターフェースは `ULipSync` 名前空間（または既存のプロジェクト名前空間）に属し、Runtime asmdef のスコープに含まれる
  - ファイル作成後、他のスクリプトから参照可能な状態でコンパイルエラーなしにビルドできる
  - _Requirements: 8.1, 8.2_

---

- [ ] 4. uLipSyncMicrophone への IAudioInputSource 実装付与
- [ ] 4.1 uLipSyncMicrophone に IAudioInputSource を実装させ、既存 API を維持する
  - `Assets/uLipSync/Runtime/uLipSyncMicrophone.cs` のクラス宣言に `: IAudioInputSource` を追加する
  - 既存の `StartRecord()`、`StopRecord()`、`isRecording` が IAudioInputSource の定義と一致することを確認し、不一致がある場合は最小限の修正を行う（既存の公開 API を変更しない）
  - 変更後、`uLipSyncMicrophone` が `IAudioInputSource` として参照可能な状態でコンパイルエラーなしにビルドできる
  - _Requirements: 8.1, 8.4_

---

- [ ] 5. uLipSyncAsioInput コンポーネントの実装
- [ ] 5.1 uLipSyncAsioInput のスケルトンとプラットフォームガードを実装する
  - `Assets/uLipSync/Runtime/uLipSyncAsioInput.cs` を新規作成する
  - クラス全体を `#if UNITY_STANDALONE_WIN || UNITY_EDITOR` で囲み、`#else` ブロックに空スタブ実装（コンパイルエラーなし）を配置する
  - `IAudioInputSource` を実装し、`[SerializeField]` フィールドとして `uLipSync lipSync`、`bool isAutoStart`、`int inputChannelOffset`（デフォルト 0）、`int inputChannelCount`（デフォルト 1）を宣言する
  - Windows 以外のプラットフォームでビルドしたときにコンパイルエラーが発生しないことを確認できる
  - _Requirements: 6.1, 6.2, 4.5_

- [ ] 5.2 ASIO デバイス列挙ロジックを実装する
  - `GetAsioDriverNames()` メソッドを実装し、`AsioOut.GetDriverNames()` を呼び出して ASIO ドライバ名の配列を返す
  - 非 Windows 環境や `AsioOut.GetDriverNames()` が例外をスローした場合は例外をキャッチして空配列を返す（クラッシュしない）
  - ドライバが 1 件以上列挙された場合に先頭デバイスを `selectedDeviceIndex = 0` としてデフォルト設定する
  - `GetAsioDriverNames()` 呼び出しでドライバ名配列が返ることを Unity Editor 上で手動確認できる
  - _Requirements: 1.1, 1.2, 1.4, 1.5_

- [ ] 5.3 プリアロケートバッファと ASIO コールバックのサンプル取得を実装する
  - 起動時（`Awake` または `OnEnable`）に 8192 フレーム × 32 チャンネルのサイズで `_preAllocBuffer` を事前確保する
  - `AudioAvailable` イベントハンドラで `e.GetAsInterleavedSamples(_preAllocBuffer)` を呼び出してサンプルを書き込み（ヒープ割り当てなし）、`inputChannelOffset` から `inputChannelCount` チャンネル分を抽出する
  - `inputChannelOffset + inputChannelCount` が ASIO デバイスの最大入力チャンネル数を超える場合、`Debug.LogWarning` を出力して利用可能な範囲にクランプする
  - コールバック内で Unity API（`Debug.Log` 等）を呼び出さない（ログはエラーフラグ経由で `Update()` に委譲する）
  - ASIO コールバック後に `_preAllocBuffer` が使い回されていることをデバッガで確認できる
  - _Requirements: 3.1, 3.2, 3.3, 3.6, 4.3_

- [ ] 5.4 録音ライフサイクル（StartRecord / StopRecord / OnEnable / OnDisable / OnDestroy）を実装する
  - `StartRecord()` で選択中のドライバ名で `AsioOut` を初期化し録音を開始する。初期化前に前回の `AsioOut` インスタンスが破棄済みであることを確認する
  - `StopRecord()` で `AsioOut.Stop()` と `AsioOut.Dispose()` を呼び出し `isRecording = false` にセットする。`try-finally` で `Dispose` を保証する
  - `isAutoStart` が `true` かつ `OnEnable` 時に有効な ASIO デバイスが選択されている場合は自動録音開始する
  - `OnDisable` および `OnDestroy` で録音中であれば `StopRecord()` を自動呼び出しする
  - 初期化失敗時（`System.Exception`）は例外をキャッチして `Debug.LogError` を出力し `isRecording = false` を維持する
  - デバイスビジー例外の場合、「デバイスが使用中です」という趣旨のエラーメッセージを `Debug.LogError` で出力する
  - 録音開始後 `isRecording` が `true` になり、停止後 `false` になることを Unity Editor の再生中インスペクタで確認できる
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 5.1, 5.2, 5.4_

- [ ] 5.5 ASIO コールバックから OnDataReceived へのパイプライン統合と Interlocked エラーフラグを実装する
  - `AudioAvailable` コールバックから `lipSync.OnDataReceived(samples, channels, sampleRate)` を `lock(_lockObject)` 内で直接呼び出す（ConcurrentQueue 等の中間バッファは使用しない）
  - ASIO ドライバから取得したサンプルレートを `sampleRate` 引数として渡す
  - コールバック内で例外が発生した場合、`Interlocked.Exchange(ref _errorFlag, 1)` でフラグをセットし、次の `Update()` でログ出力と録音停止を行う
  - `lipSync` フィールドが null の場合はデータ供給をスキップし、`Update()` で `Debug.LogWarning` を出力する（コールバック内では通知しない）
  - 録音中に `uLipSync` の MFCC 解析が動作し、フォネームイベントが発火することを Unity Editor の再生中に確認できる
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.6, 3.4, 5.3_

---

- [ ] 6. uLipSyncAsioInputEditor の実装
- [ ] 6.1 CustomEditor スケルトンとプラットフォーム別ヘルプボックスを実装する
  - `Assets/uLipSync/Editor/uLipSyncAsioInputEditor.cs` を新規作成し、`uLipSyncAsioInput` の `CustomEditor` を定義する
  - `#if UNITY_STANDALONE_WIN || UNITY_EDITOR` の条件に基づき、非 Windows プラットフォームでは「このコンポーネントは Windows Standalone / Mono 環境でのみ動作します」という趣旨のヘルプボックスを表示する
  - Windows 以外の環境でもインスペクタを描画したとき null 参照例外が発生しないことを確認できる
  - _Requirements: 9.3, 9.5_

- [ ] 6.2 インスペクタのステータス表示・デバイス更新ボタン・Start/Stop ボタンを実装する
  - 現在の `isRecording` 状態、選択デバイス名、最後に受信したコールバックのサンプル数（バッファサイズ）を読み取り専用フィールドとして表示する
  - 「デバイス更新」ボタンを実装し、押下時に `AsioOut.GetDriverNames()` を再実行してデバイスリストを更新する。ドライバが 0 件の場合は「ASIO ドライバが見つかりません」という趣旨の警告ヘルプボックスを表示する
  - `isAutoStart` が `false` かつ Unity Editor が再生中の場合に「Start Recording」および「Stop Recording」ボタンを表示し、手動録音制御を可能にする
  - インスペクタで各ボタンを押したとき期待する動作（デバイスリスト更新、録音開始/停止）が実行されることを Unity Editor で確認できる
  - _Requirements: 9.1, 9.2, 9.4, 1.3_

---

- [ ] 7. EditMode テストと MockAudioInputSource の実装
- [ ] 7.1 MockAudioInputSource を実装する
  - `Assets/uLipSync/Tests/` 以下（または適切なテスト asmdef スコープ）に `MockAudioInputSource.cs` を新規作成し、`IAudioInputSource` を実装する
  - モックは `StartRecord()`、`StopRecord()`、`isRecording` を正しく実装し、コールバック内での Unity API 呼び出しを行わない構造とする
  - テストコードから `MockAudioInputSource` をインスタンス化できることを確認できる
  - _Requirements: 8.3, 10.2, 10.4_

- [ ] 7.2 OnDataReceived 新シグネチャの EditMode テストを実装する
  - `MockAudioInputSource` を用いた EditMode テストクラスを作成し、`OnDataReceived(float[] samples, int channels, int sampleRate)` の呼び出しが正しく行われること（`samples`、`channels`、`sampleRate` の各引数）を検証する
  - `_lockObject` によるロック契約が正しく機能していること（データ競合なしにバッファへコピーされること）をモックで検証する
  - テストを Unity Test Runner の EditMode で実行したとき全テストが PASS することを確認できる
  - _Requirements: 10.2, 10.3, 4.2_

- [ ] 7.3 (P) パイプライン統合の EditMode テストを実装する
  - 実機 ASIO ハードウェアなしに ASIO パスのパイプライン統合を検証するテストを作成する
  - `MockAudioInputSource` が `uLipSync` に対してサンプルデータを供給したとき `OnDataReceived` が正しく呼び出されること、および ASIO コールバック内で Unity API が呼び出されない構造であることをテストで確認する
  - テストを Unity Test Runner の EditMode で実行したとき全テストが PASS することを確認できる
  - _Requirements: 10.2, 10.4, 8.3_
  - _Boundary: MockAudioInputSource, EditMode Tests_
  - _Depends: 7.1, 7.2_

---

- [ ] 8. 手動テストプランドキュメントの作成
- [ ] 8.1 test-plan.md を作成して実機検証手順を記載する
  - `.kiro/specs/asio-input-support/test-plan.md` を新規作成し、以下の検証手順を記載する: ASIO デバイス列挙、録音開始/停止、リップシンク動作確認（フォネームイベント発火確認）、サンプルレート補正確認、エラーケース（ドライバ不在、デバイスビジー、コールバック例外）
  - 実機 ASIO ハードウェアが利用可能な環境で test-plan.md の手順に従って全シナリオを検証できる
  - _Requirements: 10.1, 10.5_

---

- [ ] 9. README へのプラットフォーム制約・ライセンス通知の追記
- [ ] 9.1 README に Mono 専用・IL2CPP 非対応・NAudio ライセンスを明記する
  - プロジェクトの `README.md` に Unity 6000.3.10f1 および Mono スクリプティングバックエンド専用である旨、IL2CPP 非対応である旨を記載する
  - NAudio 2.x の MIT ライセンス表記および `NAudio-LICENSE.txt` の参照先を README に追記する
  - README を参照した開発者がプラットフォーム制約とライセンス義務を即座に把握できる内容になっていることを確認できる
  - _Requirements: 6.6, 6.5_
