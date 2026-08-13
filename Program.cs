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

            Environment.ExitCode = 1;
            return;
        }

        string filePath = args[0];

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine(
                $"File does not exist: {filePath}");

            Environment.ExitCode = 1;
            return;
        }

        if (!long.TryParse(args[1], out long hwndValue))
        {
            Console.Error.WriteLine(
                $"Invalid HWND: {args[1]}");

            Environment.ExitCode = 1;
            return;
        }

        IntPtr hwnd = new IntPtr(hwndValue);

        if (hwnd == IntPtr.Zero)
        {
            Console.Error.WriteLine("Invalid HWND.");

            Environment.ExitCode = 1;
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

            Environment.ExitCode = 1;
        }
    }

    private static async Task ShareFileAsync(
        string filePath,
        IntPtr hwnd)
    {
        IDataTransferManagerInterop interop =
            DataTransferManagerInterop.GetNative();

        Guid dataTransferManagerIid =
            new Guid(
                "A5CAEE9B-8708-49D1-8D36-67D25A8DA00C");

        IntPtr dataTransferManagerPtr = IntPtr.Zero;

        try
        {
            dataTransferManagerPtr =
                interop.GetForWindow(
                    hwnd,
                    ref dataTransferManagerIid);

            if (dataTransferManagerPtr == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "Unable to get DataTransferManager for the specified HWND.");
            }

            DataTransferManager dataTransferManager =
                DataTransferManager.FromAbi(
                    dataTransferManagerPtr);

            var completion =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            /*
             * Do NOT use TypedEventHandler here.
             *
             * The WinRT projection used by the project can expose
             * this event differently depending on the SDK/package
             * configuration.
             *
             * Using the normal C# event handler avoids the
             * TypedEventHandler compilation problem.
             */

            EventHandler<DataRequestedEventArgs>? handler = null;

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

                    completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
                finally
                {
                    deferral?.Complete();
                }
            };

            dataTransferManager.DataRequested += handler;

            try
            {
                /*
                 * Ask Windows to display the Share UI
                 * for our application's window.
                 */
                interop.ShowShareUIForWindow(hwnd);

                /*
                 * Wait until Windows requests the data.
                 *
                 * The timeout prevents the helper process from
                 * remaining alive forever if the Share UI is closed
                 * without requesting data.
                 */
                Task completedTask = completion.Task;

                Task timeoutTask =
                    Task.Delay(TimeSpan.FromMinutes(2));

                Task finishedTask =
                    await Task.WhenAny(
                        completedTask,
                        timeoutTask);

                if (finishedTask == timeoutTask)
                {
                    Console.Error.WriteLine(
                        "Share UI timed out or was closed.");

                    return;
                }

                /*
                 * Propagate any exception from the DataRequested
                 * event handler.
                 */
                await completion.Task;
            }
            finally
            {
                dataTransferManager.DataRequested -= handler;
            }
        }
        finally
        {
            if (dataTransferManagerPtr != IntPtr.Zero)
            {
                Marshal.Release(
                    dataTransferManagerPtr);
            }
        }
    }
}


/*
 * Windows DataTransferManager interop interface.
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
 * Native WinRT activation helper.
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