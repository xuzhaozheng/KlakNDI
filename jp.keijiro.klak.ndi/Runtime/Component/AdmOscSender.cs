using System;
#if OSC_JACK
using OscJack;
#endif
using UnityEngine;

namespace Klak.Ndi
{
    [RequireComponent(typeof(IAdmDataProvider))]
    public class AdmOscSender : MonoBehaviour
    {
        [Serializable]
        public struct AdmSettings
        {
            public float nearDistance;
            public float farDistance;

            public AdmSettings(float near, float far)
            {
                nearDistance = near;
                farDistance = far;
            }
        }
        
#if !OSC_JACK
        [Header("[Please add OSC JACK to your project > https://github.com/keijiro/OscJack]")]
#endif
        [SerializeField] private AdmSettings _settings = new AdmSettings(0.1f, 10f);
        
#if OSC_JACK

        [SerializeField] private OscConnection _connection = null;

        private OscClient _client;
        private OscConnection _customConnection;
        private IAdmDataProvider _admDataProvider;
        private object _lock = new object();
        
        private void Awake()
        {
            _admDataProvider = GetComponent<IAdmDataProvider>();
        }

        public float GetNearDistance()
        {
            lock (_lock)
                return _settings.nearDistance;
        }
        
        public float GetFarDistance()
        {
            lock (_lock) 
                return _settings.farDistance;
        }
        
        public void SetNearDistance(float value)
        {
            lock (_lock)
                _settings.nearDistance = value;
        }
        
        public void SetFarDistance(float value)
        {
            lock (_lock)
                _settings.farDistance = value;
        }
        
        private void OnAdmDataChanged(AdmData data)
        {
            SendAdm(data);
        }

        private void OnEnable()
        {
            lock (_lock)
            {
                if (_customConnection)
                {
                    _client = new OscClient(_customConnection.host, _customConnection.port);
                }
                else
                {
                    if (!_connection)
                    {
                        Debug.LogError("No connection set for OSC sender. Disabling component.");
                        enabled = false;
                        return;
                    }
                    else
                        _client = new OscClient(_connection.host, _connection.port);
                }

                if (_client != null && _admDataProvider != null)
                {
                    _admDataProvider.RegisterAdmDataChangedEvent(OnAdmDataChanged);
                }
            }
        }

        private void OnDisable()
        {
            lock (_lock)
            {
                if (_client != null)
                    _client.Dispose();
                _client = null;

                if (_admDataProvider != null)
                {
                    _admDataProvider.UnregisterAdmDataChangedEvent(OnAdmDataChanged);
                }
            }
        }

        public void SetSettings(AdmSettings settings)
        {
            lock (_lock)
                _settings = settings;
        }

        private void SendPosition(Vector3 pos, int id)
        {
            pos = pos.normalized * Mathf.Max(0.001f,
                Mathf.InverseLerp(_settings.nearDistance, _settings.farDistance, pos.magnitude));
            _client.Send($"/adm/obj/{id.ToString()}/xyz", pos.x, pos.z, pos.y);
            _client.Send($"/adm/obj/{id.ToString()}/x", pos.x);
            _client.Send($"/adm/obj/{id.ToString()}/y", pos.y);
            _client.Send($"/adm/obj/{id.ToString()}/z", pos.z);
        }

        private void SendPosition_Spherical(Vector3 pos, int id)
        {
            float azim = 0;
            float elev = 0;
            // Calculate azimuth and elevation
            if (pos.x != 0 || pos.z != 0)
            {
                azim = Mathf.Atan2(pos.x, pos.z) * Mathf.Rad2Deg;
                elev = Mathf.Atan2(pos.y, Mathf.Sqrt(pos.x * pos.x + pos.z * pos.z)) * Mathf.Rad2Deg;
            }

            float dist = Mathf.Max(0.001f,
                Mathf.InverseLerp(_settings.nearDistance, _settings.farDistance, pos.magnitude));
            _client.Send($"/adm/obj/{id.ToString()}/azim", azim);
            _client.Send($"/adm/obj/{id.ToString()}/elev", elev);
            _client.Send($"/adm/obj/{id.ToString()}/dist", dist);
        }

        private void SendGain(float gain, int id)
        {
            _client.Send($"/adm/obj/{id.ToString()}/gain", gain);
        }

        private void SendAdm(AdmData data)
        {
            lock (_lock)
            {
                if (_client == null)
                    return;
                
                int id = 1;
                foreach (var pos in data.positions)
                {
                    SendPosition(pos, id);
                    SendPosition_Spherical(pos, id);
                    id++;
                }

                id = 1;
                foreach (var gain in data.gains)
                {
                    SendGain(gain, id);
                    id++;
                }
            }
        }

        public void OnDestroy()
        {
            lock (_lock)
            {
                _client?.Dispose();
                _client = null;
            }
            
            if (_customConnection)
                Destroy(_customConnection);
        }

        public void GetHostIpAndPort(out string ipText, out int port)
        {
            if (_customConnection)
            {
                ipText = _customConnection.host;
                port = _customConnection.port;
            }
            else
            {
                ipText = _connection.host;
                port = _connection.port;
            }
        }

        public void ChangeHostIpAndPort(string ipText, int port)
        {
            if (!_customConnection)
            {
                _customConnection = ScriptableObject.CreateInstance<OscConnection>();
            }

            _customConnection.host = ipText;
            _customConnection.port = port;
            lock (_lock)
            {
                if (_client != null)
                    _client.Dispose();
                _client = new OscClient(_customConnection.host, _customConnection.port);
            }
        }
#endif
    }
}