using System;
using System.Runtime.InteropServices;
using Klak.Ndi.Audio;
using UnityEngine;
using UnityEngine.Rendering;

namespace Klak.Ndi {

[ExecuteInEditMode]
public sealed partial class NdiSender : MonoBehaviour
{
    #region Sender objects

    Interop.Send _send;
    ReadbackPool _pool;
    FormatConverter _converter;
    System.Action<AsyncGPUReadbackRequest> _onReadback;

    void PrepareSenderObjects()
    {
        // Game view capture method: Borrow the shared sender instance.
        if (_send == null && captureMethod == CaptureMethod.GameView)
            _send = SharedInstance.GameViewSend;

        // Private object initialization
        if (_send == null) _send = Interop.Send.Create(ndiName);
        if (_pool == null) _pool = new ReadbackPool();
        if (_converter == null) _converter = new FormatConverter(_resources);
        if (_onReadback == null) _onReadback = OnReadback;
    }

    void ReleaseSenderObjects()
    {
        // Total synchronization: This may cause a frame hiccup, but it's
        // needed to dispose the readback buffers safely.
        AsyncGPUReadback.WaitAllRequests();

        // Game view capture method: Leave the sender instance without
        // disposing (we're not the owner) but synchronize it. It's needed to
        // dispose the readback buffers safely too.
        if (SharedInstance.IsGameViewSend(_send))
        {
            _send.SendVideoAsync(); // Sync by null-send
            _send = null;
        }

        // Private objet disposal
        _send?.Dispose();
        _send = null;

        _pool?.Dispose();
        _pool = null;

        _converter?.Dispose();
        _converter = null;

        // We don't dispose _onReadback because it's reusable.
    }

    #endregion
    
    #region Sound Sender
    private int numSamples = 0;
    private int numChannels = 0;
    private float[] samples = new float[1];
    private int sampleRate = 44100;
    private AudioMode _audioMode;
    private Vector3 _listenerPosition;
    private object _lockObj = new object();
    private IntPtr _metaDataPtr = IntPtr.Zero;
    private bool _useVirtualSpeakerListeners = false;
    private float[] _channelVisualisations;

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying)
            return;
        
        var listenersPositions = VirtualAudio.GetListenersPositions();
        var listenerVolumes = VirtualAudio.GetListenersVolume();
        Gizmos.color = Color.yellow;
        int listIndex = 0;
        foreach (var listener in listenersPositions)
        {
            Gizmos.DrawWireSphere(transform.position + listener, 1f);
            // Add text label
            UnityEditor.Handles.Label(transform.position + listener + new Vector3(2f, 0, 0f), "Channel: "+listIndex+ System.Environment.NewLine + "Volume: "+listenerVolumes[listIndex]);
            listIndex++;
        }
    }
#endif
    
    private void ClearVirtualSpeakerListeners()
    {
        _useVirtualSpeakerListeners = false;
        VirtualAudio.useVirtualAudio = false;
        VirtualAudio.ClearAllVirtualSpeakerListeners();
    }
    
    private void CreateAudioSetup_Quad()
    {
        VirtualAudio.useVirtualAudio = true;
        VirtualAudio.ClearAllVirtualSpeakerListeners();

        float distance = virtualListenerDistance;
        float dotAdjust = 0.5f;
        VirtualAudio.AddListener( new Vector3(-distance, 0f, distance), dotAdjust, 1f);
        VirtualAudio.AddListener( new Vector3(distance, 0f, distance), dotAdjust, 1f);
        
        VirtualAudio.AddListener( new Vector3(-distance, 0f, -distance), dotAdjust, 1f);
        VirtualAudio.AddListener( new Vector3(distance, 0f, -distance), dotAdjust, 1f);
        _useVirtualSpeakerListeners = true;
    }    
    
    private void CreateAudioSetup_5point1()
    {
        VirtualAudio.useVirtualAudio = true;
        VirtualAudio.ClearAllVirtualSpeakerListeners();

        float distance = virtualListenerDistance;
        float dotAdjust = 0.5f;
        VirtualAudio.AddListener( new Vector3(-distance, 0f, distance), dotAdjust, 1f);
        VirtualAudio.AddListener( new Vector3(distance, 0f, distance), dotAdjust, 1f);
        
        VirtualAudio.AddListener( new Vector3(0f, 0f, distance), dotAdjust, 1f);
        VirtualAudio.AddListener( new Vector3(0f, 0f, 0f), dotAdjust, 0f);
        
        VirtualAudio.AddListener( new Vector3(-distance, 0f, -distance), dotAdjust, 1f);
        VirtualAudio.AddListener( new Vector3(distance, 0f, -distance), dotAdjust, 1f);
        _useVirtualSpeakerListeners = true;
    }

    private void CreateAudioSetup_7point1()
    {
        VirtualAudio.useVirtualAudio = true;
        VirtualAudio.ClearAllVirtualSpeakerListeners();

        float distance = virtualListenerDistance;
        float dotAdjust = 0.5f;
        VirtualAudio.AddListener( new Vector3(-distance, 0f, distance), dotAdjust, 1f);
        VirtualAudio.AddListener( new Vector3(distance, 0f, distance), dotAdjust, 1f);
        
        VirtualAudio.AddListener( new Vector3(0f, 0f, distance), dotAdjust, 1f);
        VirtualAudio.AddListener( new Vector3(0f, 0f, 0f), dotAdjust, 0f);

        VirtualAudio.AddListener( new Vector3(-distance, 0f, 0), dotAdjust, 1f);
        VirtualAudio.AddListener( new Vector3(distance, 0f, 0), dotAdjust, 1f);
        
        VirtualAudio.AddListener( new Vector3(-distance, 0f, -distance), dotAdjust, 1f);
        VirtualAudio.AddListener( new Vector3(distance, 0f, -distance), dotAdjust, 1f);
        _useVirtualSpeakerListeners = true;
    }

    private void CreateAudioSetup_32Array()
    {
        VirtualAudio.useVirtualAudio = true;
        VirtualAudio.ClearAllVirtualSpeakerListeners();

        float dotAdjust = 0.90f;
        
        for (int i = 0; i < 32; i++)
        {
            // Add 32 virtual speakers in a circle around the listener
            float angle = i * Mathf.PI * 2 / 32;
            float x = Mathf.Cos(angle) * virtualListenerDistance;
            float z = Mathf.Sin(angle) * virtualListenerDistance;
            VirtualAudio.AddListener(new Vector3(x, 0f, z), dotAdjust, 1f);
        }
        _useVirtualSpeakerListeners = true;
    }

    private void CreateAudioSetup_bySpeakerConfig()
    {
        VirtualAudio.ClearAllVirtualSpeakerListeners();
        if (!customSpeakerConfig)
        {
            Debug.LogError("No custom speaker config assigned!");
            return;
        }
        VirtualAudio.useVirtualAudio = true;

        var allSpeakers = customSpeakerConfig.GetAllSpeakers();
        for (int i = 0; i < allSpeakers.Length; i++)
        {
            VirtualAudio.AddListener(allSpeakers[i].position, 0.5f, allSpeakers[i].volume);
        }
        _useVirtualSpeakerListeners = true;
    }
    
    private void Update()
    {
        lock (_lockObj)
        {
            _listenerPosition = transform.position;
        }
    }
    
    private object _channelVisualisationsLock = new object();
    
    internal float[] GetChannelVisualisations()
    {
        lock (_channelVisualisationsLock)
            return _channelVisualisations;
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (_audioMode == AudioMode.AudioListener)
        {
            SendAudioListenerData(data, channels);
        }
        else if (_useVirtualSpeakerListeners)
        {
            SendCustomListenerData();
        }
    }
    
    private void SendCustomListenerData()
    {
        var mixedAudio = VirtualAudio.GetMixedAudio(out int samplesCount, _listenerPosition, useCameraPositionForVirtualAttenuation);
        int channels = mixedAudio.Count;
        lock (_channelVisualisationsLock)
            Util.UpdateVUMeter(ref _channelVisualisations, mixedAudio);
        
        unsafe
        {
            bool settingsChanged = false;
            int tempSamples = samplesCount;

            if (tempSamples != numSamples)
            {
                settingsChanged = true;
                numSamples = tempSamples;
                
            }

            if (channels != numChannels)
            {
                settingsChanged = true;
                numChannels = channels;
            }

            if (settingsChanged)
            {
                System.Array.Resize<float>(ref samples, numSamples * numChannels);
                UpdateAudioMetaData();
            }

            for (int ch = 0; ch < numChannels; ch++)
            {
                for (int i = 0; i < numSamples; i++)
                {
                    samples[numSamples * ch + i] = mixedAudio[ch][i];
                }
            }

            fixed (float* p = samples)
            {
                var frame = new Interop.AudioFrame
                {
                    SampleRate = sampleRate,
                    NoChannels = channels,
                    NoSamples = numSamples,
                    ChannelStrideInBytes = numSamples * sizeof(float),
                    Data = (System.IntPtr)p
                };
                
                frame._Metadata = _metaDataPtr;

                if (_send != null)
                {
                    _send.SendAudio(frame);
                }
            }
        }
    }

    private void UpdateAudioMetaData()
    {
        if (_metaDataPtr != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(_metaDataPtr);
            _metaDataPtr = IntPtr.Zero;
        }

        var xml = AudioMeta.GenerateSpeakerConfigXmlMetaData();
        _metaDataPtr = Marshal.StringToCoTaskMemAnsi(xml);    
    }

    private void SendAudioListenerData(float[] data, int channels)
    {
        if (data.Length == 0 || channels == 0) return;

        unsafe
        {
            bool settingsChanged = false;
            int tempSamples = data.Length / channels;

            if (tempSamples != numSamples)
            {
                settingsChanged = true;
                numSamples = tempSamples;
                
                //PluginEntry.SetNumSamples(_plugin, numSamples);
            }

            if (channels != numChannels)
            {
                settingsChanged = true;
                numChannels = channels;
                //PluginEntry.SetAudioChannels(_plugin, channels);
            }

            if (settingsChanged)
            {
                System.Array.Resize<float>(ref samples, numSamples * numChannels);
            }

            for (int ch = 0; ch < numChannels; ch++)
            {
                for (int i = 0; i < numSamples; i++)
                {
                    samples[numSamples * ch + i] = data[i * numChannels + ch];
                }
            }

            fixed (float* p = samples)
            {
                //PluginEntry.SetAudioData(_plugin, (IntPtr)p);
                var frame = new Interop.AudioFrame
                {
                    SampleRate = sampleRate,
                    NoChannels = channels,
                    NoSamples = numSamples,
                    ChannelStrideInBytes = numSamples * sizeof(float),
                    Data = (System.IntPtr)p
                };

                if (_send != null)
                {
                    _send.SendAudio(frame);
                }
            }

            //if (audioEnabled && pluginReady) PluginEntry.SendAudio(_plugin);
        }
    }

    #endregion    

    #region Capture coroutine for the Texture/GameView capture methods

    System.Collections.IEnumerator CaptureCoroutine()
    {
        for (var eof = new WaitForEndOfFrame(); true;)
        {
            // Wait for the end of the frame.
            yield return eof;

            PrepareSenderObjects();

            // Texture capture method
            if (captureMethod == CaptureMethod.Texture && sourceTexture != null)
            {
                var (w, h) = (sourceTexture.width, sourceTexture.height);

                // Pixel format conversion
                var buffer = _converter.Encode(sourceTexture, keepAlpha, true);

                // Readback entry allocation and request
                _pool.NewEntry(w, h, keepAlpha, metadata)
                     .RequestReadback(buffer, _onReadback);
            }

            // Game View capture method
            if (captureMethod == CaptureMethod.GameView)
            {
                // Game View screen capture with a temporary RT
                var (w, h) = (Screen.width, Screen.height);
                var tempRT = RenderTexture.GetTemporary(w, h, 0);
                ScreenCapture.CaptureScreenshotIntoRenderTexture(tempRT);

                // Pixel format conversion
                var buffer = _converter.Encode(tempRT, keepAlpha, false);
                RenderTexture.ReleaseTemporary(tempRT);

                // Readback entry allocation and request
                _pool.NewEntry(w, h, keepAlpha, metadata)
                     .RequestReadback(buffer, _onReadback);
            }
        }
    }

    #endregion

    #region SRP camera capture callback for the Camera capture method

    void OnCameraCapture(RenderTargetIdentifier source, CommandBuffer cb)
    {
        // A SRP may call this callback after object destruction. We can
        // exclude those cases by null-checking _attachedCamera.
        if (_attachedCamera == null) return;

        PrepareSenderObjects();

        // Pixel format conversion
        var (w, h) = (sourceCamera.pixelWidth, sourceCamera.pixelHeight);
        var buffer = _converter.Encode(cb, source, w, h, keepAlpha, true);

        // Readback entry allocation and request
        _pool.NewEntry(w, h, keepAlpha, metadata)
             .RequestReadback(buffer, _onReadback);
    }

    #endregion

    #region GPU readback completion callback

    unsafe void OnReadback(AsyncGPUReadbackRequest req)
    {
        // Readback entry retrieval
        var entry = _pool.FindEntry(req.GetData<byte>());
        if (entry == null) return;

        // Invalid state detection
        if (req.hasError || _send == null || _send.IsInvalid || _send.IsClosed)
        {
            // Do nothing but release the readback entry.
            _pool.Free(entry);
            return;
        }

        // Frame data
        // Frame data setup
        var frame = new Interop.VideoFrame
        {
            Width = entry.Width,
            Height = entry.Height,
            LineStride = entry.Width * 2,
            FourCC = entry.FourCC,
            FrameFormat = Interop.FrameFormat.Progressive,
            Data = entry.ImagePointer,
            _Metadata = entry.MetadataPointer
        };

        // Async-send initiation
        // This causes a synchronization for the last frame -- i.e., It locks
        // the thread if the last frame is still under processing.
        _send.SendVideoAsync(frame);

        // We don't need the last frame anymore. Free it.
        _pool.FreeMarkedEntry();

        // Mark this frame to get freed in the next frame.
        _pool.Mark(entry);
    }

    #endregion

    #region Component state controller

    Camera _attachedCamera;
    
    
    // Component state reset without NDI object disposal
    internal void ResetState(bool willBeActive)
    {
        _audioMode = audioMode;
        ClearVirtualSpeakerListeners();
        switch (audioMode)
        {
            case AudioMode.AudioListener:
                break;
            /*
            case AudioMode.TryOrForce5point1:
                audioSettings.speakerMode = AudioSpeakerMode.Mode5point1;
                AudioSettings.Reset(audioSettings);
                
                if (availableAudioChannels != 6)
                    CreateAudioSetup_5point1();
                break;
            case AudioMode.TryOrForce7point1:
                audioSettings.speakerMode = AudioSpeakerMode.Mode7point1;
                AudioSettings.Reset(audioSettings);
                
                if (availableAudioChannels != 8)
                    CreateAudioSetup_7point1();
                break;
            case AudioMode.TryOrForceQuad:
                audioSettings.speakerMode = AudioSpeakerMode.Quad;
                AudioSettings.Reset(audioSettings);

                if (availableAudioChannels != 4)
                    CreateAudioSetup_Quad();
                break;
            */
            case AudioMode.VirtualQuad:
                CreateAudioSetup_Quad();
                break;
            case AudioMode.Virtual5Point1:
                CreateAudioSetup_5point1();
                break;
            case AudioMode.Virtual7Point1:
                CreateAudioSetup_7point1();
                break;
            case AudioMode.Virtual32Array:
                CreateAudioSetup_32Array();
                break;
            case AudioMode.CustomConfig:
                CreateAudioSetup_bySpeakerConfig();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        
        // Camera capture coroutine termination
        // We use this to kill only a single coroutine. It may sound like
        // overkill, but I think there is no side effect in doing so.
        StopAllCoroutines();

        #if KLAK_NDI_HAS_SRP

        // A SRP may call this callback after camera destruction. We can
        // exclude those cases by null-checking _attachedCamera.
        if (_attachedCamera != null)
            CameraCaptureBridge.RemoveCaptureAction(_attachedCamera, OnCameraCapture);

        #endif

        _attachedCamera = null;

        // The following part of code is to activate the subcomponents. We can
        // break here if willBeActive is false.
        if (!willBeActive) return;

        if (captureMethod == CaptureMethod.Camera)
        {
            #if KLAK_NDI_HAS_SRP

            // Camera capture callback setup
            if (sourceCamera != null)
                CameraCaptureBridge.AddCaptureAction(sourceCamera, OnCameraCapture);

            #endif

            _attachedCamera = sourceCamera;
        }
        else
        {
            // Capture coroutine initiation
            StartCoroutine(CaptureCoroutine());
        }
    }

    // Component state reset with NDI object disposal
    internal void Restart(bool willBeActivate)
    {
        
        if (_metaDataPtr != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(_metaDataPtr);
            _metaDataPtr = IntPtr.Zero;
        }
        
        sampleRate = AudioSettings.outputSampleRate;
        
        // Debug.Log("Driver capabilties: " + AudioSettings.driverCapabilities);
        ResetState(willBeActivate);
        ReleaseSenderObjects();
    }

    internal void ResetState() => ResetState(isActiveAndEnabled);
    internal void Restart() => Restart(isActiveAndEnabled);

    #endregion

    #region MonoBehaviour implementation

    void OnEnable() => ResetState();
    void OnDisable() => Restart(false);
    void OnDestroy() => Restart(false);

    #endregion
}

} // namespace Klak.Ndi
