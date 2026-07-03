using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Sumbo.Native;

/// <summary>
/// P/Invoke wrappers for the DWM thumbnail API (dwmapi.dll). All functions
/// return an HRESULT (PreserveSig); callers check for S_OK (0).
/// </summary>
[SupportedOSPlatform("windows")]
public static class Dwm
{
    private const string DwmApi = "dwmapi.dll";

    /// <summary>S_OK.</summary>
    public const int Ok = 0;

    [DllImport(DwmApi, PreserveSig = true)]
    public static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumbId);

    [DllImport(DwmApi, PreserveSig = true)]
    public static extern int DwmUnregisterThumbnail(IntPtr thumbId);

    [DllImport(DwmApi, PreserveSig = true)]
    public static extern int DwmUpdateThumbnailProperties(IntPtr thumbId, ref DWM_THUMBNAIL_PROPERTIES props);

    [DllImport(DwmApi, PreserveSig = true)]
    public static extern int DwmQueryThumbnailSourceSize(IntPtr thumbId, out SIZE size);

    [DllImport(DwmApi, PreserveSig = true)]
    public static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);

    /// <summary>True when DWM composition is enabled (execution guard, 요건정의서 §13).</summary>
    public static bool IsCompositionEnabled()
        => DwmIsCompositionEnabled(out bool enabled) == Ok && enabled;
}
