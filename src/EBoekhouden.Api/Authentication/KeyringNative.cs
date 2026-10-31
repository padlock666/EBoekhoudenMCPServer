using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace EBoekhouden.Api.Authentication;

// Only non-secret metadata is marshalled as strings. Passwords stay in native memory.
internal static class KeyringNative
{
    internal const string Glib = "libglib-2.0.so.0";
    private const string Secret = "libsecret-1.so.0";
    private const string Gio = "libgio-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";

    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern PasswordHandle secret_password_lookupv_nonpageable_sync(
        nint schema, NativeAttributes attributes, CancellableHandle cancellable, out nint error);

    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void secret_password_free(nint password);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nint g_hash_table_new_full(nint hash, nint equal, nint keyDestroy, nint valueDestroy);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern int g_hash_table_insert(nint table, nint key, nint value);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void g_hash_table_unref(nint table);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nint g_strdup([MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void g_error_free(nint error);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern nint g_cancellable_new();

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void g_cancellable_cancel(nint cancellable);

    [DllImport(GObject, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern void g_object_unref(nint obj);
}

internal sealed class PasswordHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    // Used by the P/Invoke marshaller for the returned pointer.
    public PasswordHandle() : base(ownsHandle: true) { }

    protected override bool ReleaseHandle()
    {
        KeyringNative.secret_password_free(handle); // Clear the password memory, then free it.
        return true;
    }
}

internal sealed class CancellableHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal CancellableHandle() : base(ownsHandle: true) => SetHandle(KeyringNative.g_cancellable_new());

    protected override bool ReleaseHandle()
    {
        KeyringNative.g_object_unref(handle);
        return true;
    }
}

internal sealed class NativeAttributes : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly nint library;

    internal NativeAttributes(string application, string account) : base(ownsHandle: true)
    {
        library = NativeLibrary.Load(KeyringNative.Glib);
        try
        {
            var free = NativeLibrary.GetExport(library, "g_free");
            SetHandle(KeyringNative.g_hash_table_new_full(
                NativeLibrary.GetExport(library, "g_str_hash"),
                NativeLibrary.GetExport(library, "g_str_equal"), free, free));
            // The table owns these GLib allocations and frees them with g_free.
            KeyringNative.g_hash_table_insert(handle, KeyringNative.g_strdup("application"), KeyringNative.g_strdup(application));
            KeyringNative.g_hash_table_insert(handle, KeyringNative.g_strdup("account"), KeyringNative.g_strdup(account));
        }
        catch
        {
            if (!IsInvalid)
                KeyringNative.g_hash_table_unref(handle);
            SetHandleAsInvalid();
            NativeLibrary.Free(library);
            throw;
        }
    }

    protected override bool ReleaseHandle()
    {
        KeyringNative.g_hash_table_unref(handle);
        NativeLibrary.Free(library);
        return true;
    }
}
