using System;
using System.Runtime.InteropServices;
using DirectN;
using VCamNetSampleSource.Utilities;

namespace VCamNetSampleSource
{
    [ComVisible(true), Guid(Shared.CLSID_VCamNet)]
    public class Activator : MFAttributes, IMFActivateImpl, ICustomQueryInterface
    {
        private readonly object _sperre = new();
        private MediaSource? _quelle;
        public Activator()
        {
            EventProvider.LogInfo($"ctor commandLine:{Environment.CommandLine}");
            SetDefaultAttributes(this);
        }

        private static void SetDefaultAttributes(IMFAttributes attributes)
        {
            attributes.SetUINT32(MFConstants.MF_VIRTUALCAMERA_PROVIDE_ASSOCIATED_CAMERA_SOURCES, 1).ThrowOnError();
            attributes.SetGUID(MFConstants.MFT_TRANSFORM_CLSID_Attribute, typeof(Activator).GUID).ThrowOnError();
        }

        public HRESULT ActivateObject(Guid riid, out nint ppv)
        {
            try
            {
                EventProvider.LogInfo($"{riid}");
                if (riid == typeof(IMFMediaSourceEx).GUID || riid == typeof(IMFMediaSource).GUID)
                {
                    lock (_sperre)
                    {
                        if (_quelle == null)
                        {
                            GetUINT32(Shared.Bildbreite, out var breite).ThrowOnError();
                            GetUINT32(Shared.Bildhoehe, out var hoehe).ThrowOnError();
                            var pfad = Attributes.GetString(Shared.Bildpfad);
                            _quelle = new MediaSource(breite, hoehe, pfad);
                            CopyAllItems(_quelle).ThrowOnError();
                            SetDefaultAttributes(_quelle);
                        }
                        ppv = ComObject.QueryObjectInterface(_quelle, riid, false);
                        if (ppv != IntPtr.Zero) return HRESULTS.S_OK;
                    }
                }

                ppv = 0;
                EventProvider.LogInfo($"{riid} => E_NOINTERFACE");
                return HRESULTS.E_NOINTERFACE;
            }
            catch (Exception e)
            {
                EventProvider.LogError(e.ToString());
                ppv = 0;
                return new HRESULT(Marshal.GetHRForException(e));
            }
        }

        public HRESULT ShutdownObject()
        {
            EventProvider.LogInfo();
            lock (_sperre) { _quelle?.Dispose(); _quelle = null; }
            return HRESULTS.S_OK;
        }

        public HRESULT DetachObject()
        {
            EventProvider.LogInfo();
            lock (_sperre) { _quelle = null; }
            return HRESULTS.S_OK;
        }

        public CustomQueryInterfaceResult GetInterface(ref Guid iid, out nint ppv)
        {
            EventProvider.LogInfo($"iid{iid:B}");
            ppv = 0;
            return CustomQueryInterfaceResult.NotHandled;
        }

        [ComRegisterFunction]
        public static void RegisterFunction(Type type)
        {
            EventProvider.LogInfo("type:" + type);
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type type)
        {
            EventProvider.LogInfo("type:" + type);
        }
    }
}
