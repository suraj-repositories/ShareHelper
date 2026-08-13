using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

internal class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine(
                "Usage: ShareHelper.exe <filePath> <hwnd>");
            return;
        }

        string filePath = args[0];

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine(
                $"File does not exist: {filePath}");
            return;
        }

        if (!long.TryParse(args[1], out long hwndLong))
        {
            Console.Error.WriteLine(
                $"Invalid HWND: {args[1]}");
            return;
        }

        IntPtr hwnd = new IntPtr(hwndLong);

        if (hwnd == IntPtr.Zero)
        {
            Console.Error.WriteLine("Invalid HWND.");
            return;
        }

        try
        {
            await ShareFileAsync(filePath, hwnd);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Share UI Error: {ex}");
        }
    }

    private static async Task ShareFileAsync(
        string filePath,
        IntPtr hwnd)
    {
        IDataTransferManagerInterop interop =
            DataTransferManagerInterop.GetNative();

        Guid dtmIid =
            new Guid("A5CAEE9B-8708-49D1-8D36-67D25A8DA00C");

        IntPtr dtmPtr = IntPtr.Zero;

        try
        {
            dtmPtr = interop.GetForWindow(
                hwnd,
                ref dtmIid);

            if (dtmPtr == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "GetForWindow returned a null DataTransferManager.");
            }

            DataTransferManager dtm =
                DataTransferManager.FromAbi(dtmPtr);

            var completed =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            // IMPORTANT:
            // Use the delegate type from the Windows projection.
            TypedEventHandler<DataTransferManager, DataRequestedEventArgs>
                handler = null!;

            handler = async (sender, args) =>
            {
                DataRequestDeferral? deferral = null;

                try
                {
                    deferral =
                        args.Request.GetDeferral();

                    args.Request.Data.Properties.Title =
                        Path.GetFileName(filePath);

                    StorageFile storageFile =
                        await StorageFile.GetFileFromPathAsync(
                            filePath);

                    args.Request.Data.SetStorageItems(
                        new List<IStorageItem>
                        {
                            storageFile
                        });

                    completed.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    completed.TrySetException(ex);
                }
                finally
                {
                    deferral?.Complete();
                }
            };

            dtm.DataRequested += handler;

            try
            {
                interop.ShowShareUIForWindow(hwnd);

                await completed.Task;
            }
            finally
            {
                dtm.DataRequested -= handler;
            }
        }
        finally
        {
            if (dtmPtr != IntPtr.Zero)
            {
                Marshal.Release(dtmPtr);
            }
        }
    }
}


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
        Guid interopGuid =
            typeof(IDataTransferManagerInterop).GUID;

        int hr =
            RoGetActivationFactory(
                "Windows.ApplicationModel.DataTransfer.DataTransferManager",
                ref interopGuid,
                out IntPtr factory);

        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        try
        {
            return (IDataTransferManagerInterop)
                Marshal.GetObjectForIUnknown(factory);
        }
        finally
        {
            Marshal.Release(factory);
        }
    }
}