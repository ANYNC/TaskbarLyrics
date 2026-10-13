using System.Runtime.InteropServices;
using Windows.Graphics.Effects;
using Windows.UI.Composition;
using WinRT;

namespace TaskbarLyrics.App;

// Public COM-visible types are required for the CLR CCW. Keep the classic COM ABI isolated here;
// the Windows SDK projection does not expose IGraphicsEffectD2D1Interop.
// Contract: https://learn.microsoft.com/windows/win32/api/windows.graphics.effects.interop/
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class CompositionGaussianBlurEffect : CompositionGaussianBlurEffect.IInspectableAbi,
    CompositionGaussianBlurEffect.IGraphicsEffectAbi, CompositionGaussianBlurEffect.IGraphicsEffectSourceAbi,
    CompositionGaussianBlurEffect.IGraphicsEffectD2D1Interop
{
    internal const string AmountProperty = "Blur.BlurAmount";
    internal const string SourceParameter = "Backdrop";
    private const int InvalidArgument = unchecked((int)0x80070057);
    private static readonly string[] AnimatedProperties = [AmountProperty];
    private static readonly Guid PropertyValueId = new("4BD682DD-7554-40E9-9A9B-82654EDE7E62");
    private static readonly Guid GaussianBlurId = new("1FEB6D69-2FE6-4AC9-8C58-1D7F93E7A6A5");
    private readonly IGraphicsEffectSource _source = new CompositionEffectSourceParameter(SourceParameter);
    private string _name = "Blur";

    internal CompositionEffectFactory CreateFactory(Compositor compositor)
    {
        // Wrap the classic CCW as a projected WinRT effect. This preserves both the IInspectable
        // identity and D2D interop QI without adding a second graphics runtime or Win2D package.
        var pointer = Marshal.GetComInterfaceForObject(this, typeof(IGraphicsEffectAbi));
        try
        {
            var effect = MarshalInterface<IGraphicsEffect>.FromAbi(pointer);
            return compositor.CreateEffectFactory(effect, AnimatedProperties);
        }
        finally { Marshal.Release(pointer); }
    }

    public int GetIids(out uint count, out IntPtr ids)
    {
        count = 2;
        ids = Marshal.AllocCoTaskMem(32);
        Marshal.StructureToPtr(typeof(IGraphicsEffectAbi).GUID, ids, false);
        Marshal.StructureToPtr(typeof(IGraphicsEffectSourceAbi).GUID, IntPtr.Add(ids, 16), false);
        return 0;
    }
    public int GetRuntimeClassName(out IntPtr name)
    {
        name = MarshalString.FromManaged("TaskbarLyrics.CompositionGaussianBlurEffect");
        return 0;
    }
    public int GetTrustLevel(out int trust) { trust = 0; return 0; }
    public int GetName(out IntPtr name) { name = MarshalString.FromManaged(_name); return 0; }
    public int SetName(IntPtr name) { _name = MarshalString.FromAbi(name); return 0; }
    public int GetEffectId(out Guid id) { id = GaussianBlurId; return 0; }
    public int GetNamedPropertyMapping(string name, out uint index, out uint mapping)
    {
        index = name switch
        {
            "BlurAmount" => 0,
            "Optimization" => 1,
            "BorderMode" => 2,
            _ => uint.MaxValue
        };
        mapping = 1; // GRAPHICS_EFFECT_PROPERTY_MAPPING_DIRECT
        return index == uint.MaxValue ? InvalidArgument : 0;
    }
    public int GetPropertyCount(out uint count) { count = 3; return 0; }
    public int GetProperty(uint index, out IntPtr value)
    {
        value = IntPtr.Zero;
        if (index > 2) return InvalidArgument;
        // Standard deviation is Single; balanced optimization and hard border mode are UInt32.
        // Hard borders extend the input pixels instead of introducing transparent edge halos.
        object boxed = index == 0 ? (object)0f : (uint)1;
        var inspectable = MarshalInspectable<object>.FromManaged(boxed);
        try
        {
            var id = PropertyValueId;
            return Marshal.QueryInterface(inspectable, ref id, out value);
        }
        finally { Marshal.Release(inspectable); }
    }
    public int GetSource(uint index, out IntPtr source)
    {
        source = index == 0 ? MarshalInterface<IGraphicsEffectSource>.FromManaged(_source) : IntPtr.Zero;
        return index == 0 ? 0 : InvalidArgument;
    }
    public int GetSourceCount(out uint count) { count = 1; return 0; }

    [ComVisible(true), Guid("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInspectableAbi
    {
        [PreserveSig] int GetIids(out uint count, out IntPtr ids);
        [PreserveSig] int GetRuntimeClassName(out IntPtr name);
        [PreserveSig] int GetTrustLevel(out int trust);
    }
    [ComVisible(true), Guid("CB51C0CE-8FE6-4636-B202-861FAA07D8F3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IGraphicsEffectAbi
    {
        [PreserveSig] int GetIids(out uint count, out IntPtr ids);
        [PreserveSig] int GetRuntimeClassName(out IntPtr name);
        [PreserveSig] int GetTrustLevel(out int trust);
        [PreserveSig] int GetName(out IntPtr name);
        [PreserveSig] int SetName(IntPtr name);
    }
    [ComVisible(true), Guid("2D8F9DDC-4339-4EB9-9216-F9DEB75658A2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IGraphicsEffectSourceAbi
    {
        [PreserveSig] int GetIids(out uint count, out IntPtr ids);
        [PreserveSig] int GetRuntimeClassName(out IntPtr name);
        [PreserveSig] int GetTrustLevel(out int trust);
    }
    [ComVisible(true), Guid("2FC57384-A068-44D7-A331-30982FCF7177"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IGraphicsEffectD2D1Interop
    {
        [PreserveSig] int GetEffectId(out Guid id);
        [PreserveSig] int GetNamedPropertyMapping([MarshalAs(UnmanagedType.LPWStr)] string name, out uint index, out uint mapping);
        [PreserveSig] int GetPropertyCount(out uint count);
        [PreserveSig] int GetProperty(uint index, out IntPtr value);
        [PreserveSig] int GetSource(uint index, out IntPtr source);
        [PreserveSig] int GetSourceCount(out uint count);
    }
}
