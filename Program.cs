using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

internal class Program
{
    private static readonly Guid DataTransferManagerIid =
        new("A5CAEE9B-8708-49D1-8D36-67D25A8DA00C");

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
            Console.Error.WriteLine(
                "Invalid HWND.");

            return 1;
        }

        Console.WriteLine(
            $"File: {filePath}");

        Console.WriteLine(
            $"HWND: 0x{hwnd.ToInt64():X}");

        try
        {
            await ShareFileAsync(
                filePath,
                hwnd);

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
         * Get the DataTransferManager activation factory.
         */
        IDataTransferManagerInterop interop =
            GetDataTransferManagerInterop();

        IntPtr dtmPtr = IntPtr.Zero;

        try
        {
            /*
             * Get DataTransferManager associated
             * with the JavaFX HWND.
             */
            dtmPtr =
                interop.GetForWindow(
                    hwnd,
                    ref DataTransferManagerIid);

            if (dtmPtr == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "GetForWindow returned NULL.");
            }

            /*
             * Convert ABI pointer into the WinRT
             * DataTransferManager object.
             */
            DataTransferManager dataTransferManager =
                MarshalInterface<DataTransferManager>
                    .FromAbi(dtmPtr);

            /*
             * Register the DataRequested event.
             */
            TypedEventHandler<
                DataTransferManager,
                DataRequestedEventArgs> handler =
                async (sender, args) =>
                {
                    DataRequestDeferral? deferral = null;

                    try
                    {
                        deferral =
                            args.Request.GetDeferral();

                        args.Request.Data.Properties.Title =
                            Path.GetFileName(filePath);

                        StorageFile storageFile =
                            await StorageFile
                                .GetFileFromPathAsync(
                                    filePath);

                        args.Request.Data.SetStorageItems(
                            new List<IStorageItem>
                            {
                                storageFile
                            });

                        args.Request.Data.RequestedOperation =
                            DataPackageOperation.Copy;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(
                            $"DataRequested Error: {ex}");
                    }
                    finally
                    {
                        deferral?.Complete();
                    }
                };

            dataTransferManager.DataRequested += handler;

            try
            {
                Console.WriteLine(
                    "Showing Windows Share UI...");

                interop.ShowShareUIForWindow(
                    hwnd);

                /*
                 * Keep the helper alive while the Share UI
                 * is active.
                 *
                 * The DataRequested event normally happens
                 * immediately when Windows prepares the
                 * available share targets.
                 */
                await Task.Delay(
                    TimeSpan.FromSeconds(30));
            }
            finally
            {
                dataTransferManager.DataRequested -=
                    handler;
            }
        }
        finally
        {
            if (dtmPtr != IntPtr.Zero)
            {
                Marshal.Release(
                    dtmPtr);
            }
        }
    }

    private static IDataTransferManagerInterop
        GetDataTransferManagerInterop()
    {
        Guid interopIid =
            typeof(IDataTransferManagerInterop).GUID;

        IntPtr factory =
            IntPtr.Zero;

        /*
         * IMPORTANT:
         *
         * RoGetActivationFactory expects an HSTRING,
         * NOT a normal LPWStr string.
         */
        
        IntPtr className;

        int stringHr =
            WindowsCreateString(
                "Windows.ApplicationModel.DataTransfer.DataTransferManager",
                "Windows.ApplicationModel.DataTransfer.DataTransferManager".Length,
                out className);

        if (stringHr < 0)
        {
            Marshal.ThrowExceptionForHR(stringHr);
        }

        try
        {
            int hr =
                RoGetActivationFactory(
                    className,
                    ref interopIid,
                    out factory);

            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            if (factory == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "RoGetActivationFactory returned NULL.");
            }

            return (IDataTransferManagerInterop)
                Marshal.GetObjectForIUnknown(
                    factory);
        }
        finally
        {
            if (factory != IntPtr.Zero)
            {
                Marshal.Release(factory);
            }

            WindowsDeleteString(
                className);
        }
    }

    [DllImport(
        "combase.dll",
        ExactSpelling = true)]
    private static extern int RoGetActivationFactory(
        IntPtr activatableClassId,
        ref Guid iid,
        out IntPtr factory);

    [DllImport(
        "combase.dll",
        ExactSpelling = true)]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)]
        string sourceString,
        int length,
        out IntPtr hstring);

    [DllImport(
        "combase.dll",
        ExactSpelling = true)]
    private static extern int WindowsDeleteString(
        IntPtr hstring);
}


/*
 * IDataTransferManagerInterop
 *
 * GUID:
 * 3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8
 */
[ComImport]
[Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
[InterfaceType(
    ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDataTransferManagerInterop
{
    IntPtr GetForWindow(
        [In] IntPtr appWindow,
        [In] ref Guid riid);

    void ShowShareUIForWindow(
        [In] IntPtr appWindow);
}