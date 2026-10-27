using System.Runtime.InteropServices;

namespace TaskbarLyrics.App;

// Prefixes of the Windows SDK UIAutomationClient.h interfaces, in ABI order.
// Unused preceding slots must remain present. No WPF UIA proxy API is involved.
[ComImport, Guid("FF48DBA4-60EF-4201-AA87-54103EEF594E"), ClassInterface(ClassInterfaceType.None)]
internal class NativeAutomation;

[ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INativeAutomation
{
    [return: MarshalAs(UnmanagedType.Bool)] bool CompareElements(INativeAutomationElement first, INativeAutomationElement second);
    [return: MarshalAs(UnmanagedType.Bool)] bool CompareRuntimeIds([MarshalAs(UnmanagedType.SafeArray)] int[] first, [MarshalAs(UnmanagedType.SafeArray)] int[] second);
    INativeAutomationElement GetRootElement();
    INativeAutomationElement ElementFromHandle(IntPtr handle);
    INativeAutomationElement ElementFromPoint(NativeAutomationPoint point);
    INativeAutomationElement GetFocusedElement();
    INativeAutomationElement GetRootElementBuildCache(INativeAutomationCacheRequest cache);
    INativeAutomationElement ElementFromHandleBuildCache(IntPtr handle, INativeAutomationCacheRequest cache);
    INativeAutomationElement ElementFromPointBuildCache(NativeAutomationPoint point, INativeAutomationCacheRequest cache);
    INativeAutomationElement GetFocusedElementBuildCache(INativeAutomationCacheRequest cache);
    [return: MarshalAs(UnmanagedType.IUnknown)] object CreateTreeWalker(INativeAutomationCondition condition);
    [return: MarshalAs(UnmanagedType.IUnknown)] object GetControlViewWalker();
    [return: MarshalAs(UnmanagedType.IUnknown)] object GetContentViewWalker();
    [return: MarshalAs(UnmanagedType.IUnknown)] object GetRawViewWalker();
    INativeAutomationCondition GetRawViewCondition();
    INativeAutomationCondition GetControlViewCondition();
    INativeAutomationCondition GetContentViewCondition();
    INativeAutomationCacheRequest CreateCacheRequest();
    INativeAutomationCondition CreateTrueCondition();
    INativeAutomationCondition CreateFalseCondition();
    INativeAutomationCondition CreatePropertyCondition(int propertyId, [MarshalAs(UnmanagedType.Struct)] object value);
    INativeAutomationCondition CreatePropertyConditionEx(int propertyId, [MarshalAs(UnmanagedType.Struct)] object value, int flags);
    INativeAutomationCondition CreateAndCondition(INativeAutomationCondition first, INativeAutomationCondition second);
    INativeAutomationCondition CreateAndConditionFromArray([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_UNKNOWN)] INativeAutomationCondition[] conditions);
    INativeAutomationCondition CreateAndConditionFromNativeArray([MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] INativeAutomationCondition[] conditions, int count);
    INativeAutomationCondition CreateOrCondition(INativeAutomationCondition first, INativeAutomationCondition second);
}

[ComImport, Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INativeAutomationElement
{
    void SetFocus();
    [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] int[] GetRuntimeId();
    INativeAutomationElement FindFirst(int scope, INativeAutomationCondition condition);
    INativeAutomationElementArray FindAll(int scope, INativeAutomationCondition condition);
    INativeAutomationElement FindFirstBuildCache(int scope, INativeAutomationCondition condition, INativeAutomationCacheRequest cache);
    INativeAutomationElementArray FindAllBuildCache(int scope, INativeAutomationCondition condition, INativeAutomationCacheRequest cache);
    INativeAutomationElement BuildUpdatedCache(INativeAutomationCacheRequest cache);
    [return: MarshalAs(UnmanagedType.Struct)] object GetCurrentPropertyValue(int propertyId);
    [return: MarshalAs(UnmanagedType.Struct)] object GetCurrentPropertyValueEx(int propertyId, [MarshalAs(UnmanagedType.Bool)] bool ignoreDefault);
    [return: MarshalAs(UnmanagedType.Struct)] object GetCachedPropertyValue(int propertyId);
}

[ComImport, Guid("14314595-B4BC-4055-95F2-58F2E42C9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INativeAutomationElementArray
{
    int GetLength();
    INativeAutomationElement GetElement(int index);
}

[ComImport, Guid("352FFBA8-0973-437C-A61F-F64CAFD81DF9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INativeAutomationCondition;

[ComImport, Guid("B32A92B5-BC25-4078-9C08-D7EE95C48E03"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INativeAutomationCacheRequest
{
    void AddProperty(int propertyId);
    void AddPattern(int patternId);
    INativeAutomationCacheRequest Clone();
    int GetTreeScope();
    void SetTreeScope(int scope);
    INativeAutomationCondition GetTreeFilter();
    void SetTreeFilter(INativeAutomationCondition filter);
    int GetAutomationElementMode();
    void SetAutomationElementMode(int mode);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeAutomationPoint
{
    public int X;
    public int Y;
}
