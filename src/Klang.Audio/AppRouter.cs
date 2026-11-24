using System.Runtime.InteropServices;

namespace Klang.Audio;

/// <summary>
/// Setter hvilken avspillingsenhet et bestemt program bruker, slik som
/// «Innstillinger &gt; System &gt; Lyd &gt; Volumblander» gjør.
/// Bruker et udokumentert Windows-grensesnitt (Windows.Media.Internal.AudioPolicyConfig),
/// det samme som EarTrumpet bruker. Hvis det ikke finnes, er <see cref="IsSupported"/> false
/// og brukeren må gjøre det selv i Innstillinger.
/// </summary>
public sealed unsafe class AppRouter : IDisposable
{
    private const string ClassId = "Windows.Media.Internal.AudioPolicyConfig";
    // Windows 10 21H2 og nyere, inkludert Windows 11.
    private static readonly Guid IidModern = new("ab3d4648-e242-459f-b02f-541c70306324");
    private static readonly Guid IidLegacy = new("2a59116d-6c4f-45e0-a74f-707e3fef9258");

    // Plass i vtabellen: IUnknown (3) + IInspectable (3) + 19 metoder vi ikke bruker.
    private const int SlotSetPersisted = 25;
    private const int SlotGetPersisted = 26;

    private const string MmDevApiToken = @"\\?\SWD#MMDEVAPI#";
    private const string RenderInterface = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    private const int FlowRender = 0;
    private const int RoleConsole = 0, RoleMultimedia = 1;

    private IntPtr _factory;

    public bool IsSupported => _factory != IntPtr.Zero;
    public string? LastError { get; private set; }

    public AppRouter()
    {
        foreach (var iid in new[] { IidModern, IidLegacy })
        {
            IntPtr cls = IntPtr.Zero;
            try
            {
                Check(WindowsCreateString(ClassId, ClassId.Length, out cls));
                var id = iid;
                if (RoGetActivationFactory(cls, ref id, out _factory) >= 0 && _factory != IntPtr.Zero) return;
            }
            catch (Exception ex) { LastError = ex.Message; }
            finally { if (cls != IntPtr.Zero) WindowsDeleteString(cls); }
        }
        LastError ??= "Windows tillot ikke å bytte lydenhet per program.";
        Klang.Core.Log.Info($"AppRouter ikke tilgjengelig: {LastError}");
    }

    /// <summary>Sender programmets lyd til enheten med MMDevice-id <paramref name="deviceId"/>.</summary>
    public bool Route(IEnumerable<int> processIds, string deviceId) => SetFor(processIds, MmDevApiToken + deviceId + RenderInterface);

    /// <summary>Lar programmet bruke Windows-standard igjen.</summary>
    public bool Reset(IEnumerable<int> processIds) => SetFor(processIds, null);

    private bool SetFor(IEnumerable<int> processIds, string? fullId)
    {
        if (!IsSupported) return false;
        bool ok = true;
        foreach (var pid in processIds)
        {
            IntPtr h = IntPtr.Zero;
            try
            {
                if (fullId is not null) Check(WindowsCreateString(fullId, fullId.Length, out h));
                var vtbl = *(IntPtr**)_factory;
                var set = (delegate* unmanaged[Stdcall]<IntPtr, uint, int, int, IntPtr, int>)vtbl[SlotSetPersisted];
                int hr1 = set(_factory, (uint)pid, FlowRender, RoleMultimedia, h);
                int hr2 = set(_factory, (uint)pid, FlowRender, RoleConsole, h);
                if (hr1 < 0 || hr2 < 0) { ok = false; LastError = $"Feilkode 0x{Math.Min(hr1, hr2):X8}"; }
                Klang.Core.Log.Info($"Ruting pid {pid} -> {(fullId ?? "standard")}: hr 0x{hr1:X8}/0x{hr2:X8}");
            }
            catch (Exception ex) { ok = false; LastError = ex.Message; }
            finally { if (h != IntPtr.Zero) WindowsDeleteString(h); }
        }
        return ok;
    }

    /// <summary>Hvilken enhet programmet er satt til, eller null for Windows-standard.</summary>
    public string? GetRoute(int processId)
    {
        if (!IsSupported) return null;
        var vtbl = *(IntPtr**)_factory;
        var get = (delegate* unmanaged[Stdcall]<IntPtr, uint, int, int, IntPtr*, int>)vtbl[SlotGetPersisted];
        IntPtr h = IntPtr.Zero;
        if (get(_factory, (uint)processId, FlowRender, RoleMultimedia, &h) < 0 || h == IntPtr.Zero) return null;
        try
        {
            uint len;
            char* p = WindowsGetStringRawBuffer(h, &len);
            return new string(p, 0, (int)len);
        }
        finally { WindowsDeleteString(h); }
    }

    public void Dispose()
    {
        if (_factory != IntPtr.Zero) { Marshal.Release(_factory); _factory = IntPtr.Zero; }
    }

    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern char* WindowsGetStringRawBuffer(IntPtr hstring, uint* length);
}
