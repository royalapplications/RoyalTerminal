// Licensed under the MIT License.
// RoyalTerminal.GhosttySharp - .NET bindings for the Ghostty terminal emulator.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

/// <summary>
/// P/Invoke declarations for the Ghostty C API using source-generated LibraryImport.
/// All functions use cdecl calling convention and leverage zero-alloc marshalling for blittable types.
/// Bool values are marshalled as byte (0=false, non-zero=true) for DisableRuntimeMarshalling compatibility.
/// </summary>
public static unsafe partial class GhosttyNative
{
    public const string LibraryName = "ghostty";
    private const string SurfaceGetRowCellsWithGraphemesSymbol = "ghostty_surface_get_row_cells_with_graphemes";

    private static readonly nint s_nativeLibraryHandle = LoadNativeLibraryHandle();
    private static readonly nint s_surfaceGetRowCellsWithGraphemesExport =
        ResolveOptionalExport(SurfaceGetRowCellsWithGraphemesSymbol);

    /// <summary>
    /// Returns true when the native Ghostty library exposes grapheme-aware row-cell reading.
    /// </summary>
    public static bool SupportsSurfaceRowCellGraphemes => s_surfaceGetRowCellsWithGraphemesExport != nint.Zero;

    private static nint LoadNativeLibraryHandle()
    {
        NativeLibraryLoader.Initialize();
        return NativeLibrary.TryLoad(LibraryName, typeof(GhosttyNative).Assembly, null, out nint handle)
            ? handle
            : nint.Zero;
    }

    private static nint ResolveOptionalExport(string symbol)
    {
        if (s_nativeLibraryHandle == nint.Zero)
        {
            return nint.Zero;
        }

        return NativeLibrary.TryGetExport(s_nativeLibraryHandle, symbol, out nint export)
            ? export
            : nint.Zero;
    }

    // -------------------------------------------------------------------
    // Initialization
    // -------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "ghostty_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Init(nuint argc, byte** argv);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_cli_try_action")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void CliTryAction();

    [LibraryImport(LibraryName, EntryPoint = "ghostty_info")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyInfo Info();

    [LibraryImport(LibraryName, EntryPoint = "ghostty_translate")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte* Translate(byte* str);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_string_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void StringFree(GhosttyString str);

    // -------------------------------------------------------------------
    // Configuration
    // -------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "ghostty_config_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint ConfigNew();

    [LibraryImport(LibraryName, EntryPoint = "ghostty_config_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void ConfigFree(nint config);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_config_clone")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint ConfigClone(nint config);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_config_load_cli_args")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void ConfigLoadCliArgs(nint config);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_config_load_file")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void ConfigLoadFile(nint config, byte* path);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_config_load_default_files")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void ConfigLoadDefaultFiles(nint config);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_config_load_recursive_files")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void ConfigLoadRecursiveFiles(nint config);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_config_finalize")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void ConfigFinalize(nint config);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_config_get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte ConfigGet(nint config, void* value, byte* key, nuint keyLen);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_config_trigger")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyInputTrigger ConfigTrigger(nint config, byte* action, nuint actionLen);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_config_diagnostics_count")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial uint ConfigDiagnosticsCount(nint config);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_config_get_diagnostic")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyDiagnostic ConfigGetDiagnostic(nint config, uint index);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_config_open_path")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyString ConfigOpenPath();

    // -------------------------------------------------------------------
    // Application
    // -------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "ghostty_app_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint AppNew(GhosttyRuntimeConfig* runtimeConfig, nint config);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_app_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void AppFree(nint app);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_app_tick")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void AppTick(nint app);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_app_userdata")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint AppUserdata(nint app);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_app_set_focus")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void AppSetFocus(nint app, byte focused);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_app_key")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte AppKey(nint app, GhosttyInputKey key);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_app_key_is_binding")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte AppKeyIsBinding(nint app, GhosttyInputKey key);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_app_keyboard_changed")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void AppKeyboardChanged(nint app);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_app_open_config")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void AppOpenConfig(nint app);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_app_update_config")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void AppUpdateConfig(nint app, nint config);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_app_needs_confirm_quit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte AppNeedsConfirmQuit(nint app);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_app_has_global_keybinds")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte AppHasGlobalKeybinds(nint app);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_app_set_color_scheme")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void AppSetColorScheme(nint app, GhosttyColorScheme scheme);

    // -------------------------------------------------------------------
    // Surface
    // -------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_config_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttySurfaceConfig SurfaceConfigNew();

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint SurfaceNew(nint app, GhosttySurfaceConfig* config);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceFree(nint surface);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_userdata")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint SurfaceUserdata(nint surface);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_app")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint SurfaceApp(nint surface);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_inherited_config")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttySurfaceConfig SurfaceInheritedConfig(nint surface, GhosttySurfaceContext context);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_update_config")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceUpdateConfig(nint surface, nint config);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_needs_confirm_quit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte SurfaceNeedsConfirmQuit(nint surface);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_process_exited")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte SurfaceProcessExited(nint surface);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_refresh")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceRefresh(nint surface);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_draw")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceDraw(nint surface);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_set_content_scale")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceSetContentScale(nint surface, double x, double y);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_set_focus")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceSetFocus(nint surface, byte focused);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_set_occlusion")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceSetOcclusion(nint surface, byte occluded);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_set_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceSetSize(nint surface, uint width, uint height);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttySurfaceSize SurfaceSize(nint surface);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_set_color_scheme")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceSetColorScheme(nint surface, GhosttyColorScheme scheme);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_key_translation_mods")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyMods SurfaceKeyTranslationMods(nint surface, GhosttyMods mods);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_key")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte SurfaceKey(nint surface, GhosttyInputKey key);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_key_is_binding")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte SurfaceKeyIsBinding(nint surface, GhosttyInputKey key, GhosttyBindingFlags* flags);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_text")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceText(nint surface, byte* text, nuint len);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_preedit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfacePreedit(nint surface, byte* text, nuint len);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_mouse_captured")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte SurfaceMouseCaptured(nint surface);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_mouse_button")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte SurfaceMouseButton(nint surface, GhosttyMouseState state, GhosttyMouseButton button, GhosttyMods mods);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_mouse_pos")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceMousePos(nint surface, double x, double y, GhosttyMods mods);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_mouse_scroll")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceMouseScroll(nint surface, double x, double y, int scrollMods);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_mouse_pressure")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceMousePressure(nint surface, uint stage, double pressure);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_ime_point")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceImePoint(nint surface, double* x, double* y, double* w, double* h);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_request_close")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceRequestClose(nint surface);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_split")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceSplit(nint surface, GhosttySplitDirection direction);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_split_focus")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceSplitFocus(nint surface, GhosttyGotoSplit direction);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_split_resize")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceSplitResize(nint surface, GhosttyResizeSplitDirection direction, ushort amount);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_split_equalize")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceSplitEqualize(nint surface);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_binding_action")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte SurfaceBindingAction(nint surface, byte* action, nuint len);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_complete_clipboard_request")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceCompleteClipboardRequest(nint surface, byte* data, nint state, byte confirmed);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_has_selection")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte SurfaceHasSelection(nint surface);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_read_selection")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte SurfaceReadSelection(nint surface, GhosttyText* text);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_read_text")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte SurfaceReadText(nint surface, GhosttySelection selection, GhosttyText* text);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_free_text")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceFreeText(nint surface, GhosttyText* text);

    // -------------------------------------------------------------------
    // Screen State Reading (Custom Rendering)
    // -------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_screen_lock")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceScreenLock(nint surface);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_screen_unlock")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceScreenUnlock(nint surface);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_cursor_info")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceCursorInfo(nint surface, GhosttyCursorInfo* info);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_get_row_cells")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial uint SurfaceGetRowCells(nint surface, uint row, GhosttyCellInfo* cells, uint maxCells);

    /// <summary>
    /// Reads row cells with optional flattened grapheme payload.
    /// Falls back to <see cref="SurfaceGetRowCells"/> when the native symbol
    /// is unavailable in the loaded Ghostty library.
    /// </summary>
    public static uint SurfaceGetRowCellsWithGraphemes(
        nint surface,
        uint row,
        GhosttyCellInfo* cells,
        uint maxCells,
        GhosttyCellGraphemeSpan* graphemeSpans,
        uint maxSpans,
        uint* graphemeCodepoints,
        uint maxGraphemeCodepoints,
        uint* graphemeCodepointsWritten)
    {
        if (s_surfaceGetRowCellsWithGraphemesExport == nint.Zero)
        {
            uint filled = SurfaceGetRowCells(surface, row, cells, maxCells);

            if (graphemeSpans != null)
            {
                uint spanCount = filled < maxSpans ? filled : maxSpans;
                for (uint i = 0; i < spanCount; i++)
                {
                    graphemeSpans[i] = default;
                }
            }

            if (graphemeCodepointsWritten != null)
            {
                *graphemeCodepointsWritten = 0;
            }

            return filled;
        }

        var fn = (delegate* unmanaged[Cdecl]<
            nint,
            uint,
            GhosttyCellInfo*,
            uint,
            GhosttyCellGraphemeSpan*,
            uint,
            uint*,
            uint,
            uint*,
            uint>)s_surfaceGetRowCellsWithGraphemesExport;

        return fn(
            surface,
            row,
            cells,
            maxCells,
            graphemeSpans,
            maxSpans,
            graphemeCodepoints,
            maxGraphemeCodepoints,
            graphemeCodepointsWritten);
    }

    // -------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "ghostty_surface_inspector")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint SurfaceInspector(nint surface);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_inspector_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void InspectorFree(nint surface);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_inspector_set_focus")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void InspectorSetFocus(nint inspector, byte focused);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_inspector_set_content_scale")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void InspectorSetContentScale(nint inspector, double x, double y);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_inspector_set_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void InspectorSetSize(nint inspector, uint width, uint height);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_inspector_mouse_button")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void InspectorMouseButton(nint inspector, GhosttyMouseState state, GhosttyMouseButton button, GhosttyMods mods);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_inspector_mouse_pos")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void InspectorMousePos(nint inspector, double x, double y);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_inspector_mouse_scroll")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void InspectorMouseScroll(nint inspector, double x, double y, int scrollMods);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_inspector_key")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void InspectorKey(nint inspector, GhosttyInputAction action, GhosttyKey key, GhosttyMods mods);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_inspector_text")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void InspectorText(nint inspector, byte* text);

    // -------------------------------------------------------------------
    // Misc
    // -------------------------------------------------------------------

    [LibraryImport(LibraryName, EntryPoint = "ghostty_set_window_background_blur")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SetWindowBackgroundBlur(nint app, nint window);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_benchmark_cli")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte BenchmarkCli(byte* arg1, byte* arg2);
}
