using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine(
                "Usage: ShareHelper.exe <filePath> <hwnd>");

            return 1;
        }

        string filePath = args[0];

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine(
                $"File does not exist: {filePath}");

            return 1;
        }

        if (!long.TryParse(args[1], out long hwndValue))
        {
            Console.Error.WriteLine(
                $"Invalid HWND: {args[1]}");

            return 1;
        }

        IntPtr hwnd = new(hwndValue);

        if (hwnd == IntPtr.Zero)
        {
            Console.Error.WriteLine("Invalid HWND.");
            return 1;
        }

        try
        {
            await ShareFileAsync(filePath, hwnd);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Share UI Error: {ex}");

            return 1;
        }
    }

    private static async Task ShareFileAsync(
        string filePath,
        IntPtr hwnd)
    {
        /*
         * Get the native DataTransferManager interop object.
         */
        IDataTransferManagerInterop interop =
            DataTransferManagerInterop.GetNative();

        /*
         * IDataTransferManager IID.
         */
        Guid dataTransferManagerIid =
            new("A5CAEE9B-8708-49D1-8D36-67D25A8DA00C");

        IntPtr dataTransferManager =
            IntPtr.Zero;

        try
        {
            /*
             * Get DataTransferManager associated with
             * the Java/Swing window HWND.
             */
            dataTransferManager =
                interop.GetForWindow(
                    hwnd,
                    ref dataTransferManagerIid);

            if (dataTransferManager == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "GetForWindow returned NULL.");
            }

            /*
             * Get the IDataTransferManager COM interface.
             */
            IDataTransferManager manager =
                (IDataTransferManager)
                    Marshal.GetObjectForIUnknown(
                        dataTransferManager);

            /*
             * Register our DataRequested handler.
             */
            DataRequestedHandler handler =
                new DataRequestedHandler(
                    filePath);

            manager.AddDataRequested(
                handler);

            try
            {
                /*
                 * Display Windows Share UI.
                 */
                interop.ShowShareUIForWindow(hwnd);

                /*
                 * Keep this process alive while Windows
                 * is displaying/using the Share UI.
                 */
                await handler.WaitAsync();
            }
            finally
            {
                manager.RemoveDataRequested(
                    handler);
            }
        }
        finally
        {
            if (dataTransferManager != IntPtr.Zero)
            {
                Marshal.Release(
                    dataTransferManager);
            }
        }
    }
}


/*
 * Native DataTransferManager interop.
 *
 * IID:
 * 3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8
 */
[ComImport]
[Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDataTransferManagerInterop
{
    IntPtr GetForWindow(
        [In] IntPtr appWindow,
        [In] ref Guid riid);

    void ShowShareUIForWindow(
        [In] IntPtr appWindow);
}


/*
 * Native IDataTransferManager.
 *
 * IID:
 * A9DA01AA-E5E7-4D55-8D7B-5F2F5A8F0B8E
 */
[ComImport]
[Guid("A9DA01AA-E5E7-4D55-8D7B-5F2F5A8F0B8E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDataTransferManager
{
    void AddDataRequested(
        [MarshalAs(UnmanagedType.Interface)]
        IDataRequestedHandler handler);

    void RemoveDataRequested(
        [MarshalAs(UnmanagedType.Interface)]
        IDataRequestedHandler handler);
}


/*
 * Native DataRequested event handler.
 */
[ComImport]
[Guid("7B1D9B0E-7E2B-4A5D-B5D5-0B2C9A7B7F8D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDataRequestedHandler
{
}


/*
 * DataRequested callback implementation.
 *
 * This class is intentionally kept alive while the Share UI
 * is active.
 */
internal sealed class DataRequestedHandler :
    IDataRequestedHandler
{
    private readonly string filePath;

    private readonly TaskCompletionSource<bool>
        completion =
            new(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);

    public DataRequestedHandler(
        string filePath)
    {
        this.filePath = filePath;
    }

    public Task WaitAsync()
    {
        return completion.Task;
    }
}


/*
 * WinRT activation.
 */
internal static class DataTransferManagerInterop
{
    [DllImport(
        "api-ms-win-core-winrt-l1-1-0.dll",
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    private static extern int RoGetActivationFactory(
        [MarshalAs(UnmanagedType.LPWStr)]
        string runtimeClassId,

        [In] ref Guid riid,

        out IntPtr factory);


    public static IDataTransferManagerInterop GetNative()
    {
        Guid iid =
            typeof(IDataTransferManagerInterop).GUID;

        int hr =
            RoGetActivationFactory(
                "Windows.ApplicationModel.DataTransfer.DataTransferManager",
                ref iid,
                out IntPtr factory);

        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        try
        {
            return (IDataTransferManagerInterop)
                Marshal.GetObjectForIUnknown(
                    factory);
        }
        finally
        {
            Marshal.Release(factory);
        }
    }
}