using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;

using WinRT;

internal class Program
{
    private static readonly Guid DtmIid =
        new Guid(
            0xA5CAEE9B,
            0x8708,
            0x49D1,
            0x8D,
            0x36,
            0x67,
            0xD2,
            0x5A,
            0x8D,
            0xA0,
            0x0C
        );

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

        if (!long.TryParse(
                args[1],
                out long hwndValue))
        {
            Console.Error.WriteLine(
                $"Invalid HWND: {args[1]}");

            return 1;
        }

        IntPtr hwnd =
            new IntPtr(hwndValue);

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
         * Get DataTransferManager activation factory.
         */
        IDataTransferManagerInterop interop =
            DataTransferManager
                .As<IDataTransferManagerInterop>();

        /*
         * IMPORTANT:
         *
         * A static readonly Guid cannot be passed
         * directly using ref.
         *
         * Make a local copy.
         */
        Guid dtmIid = DtmIid;

        /*
         * Get DataTransferManager for the
         * JavaFX HWND.
         */
        IntPtr result =
            interop.GetForWindow(
                hwnd,
                ref dtmIid);

        if (result == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "GetForWindow returned NULL.");
        }

        /*
         * Convert ABI pointer to managed
         * DataTransferManager.
         */
        DataTransferManager dataTransferManager =
            MarshalInterface<DataTransferManager>
                .FromAbi(result);

        /*
         * Register DataRequested handler.
         *
         * This is called by Windows when the
         * Share UI requests the actual data.
         */
        TypedEventHandler<
            DataTransferManager,
            DataRequestedEventArgs> handler =
            async (sender, args) =>
            {
                DataRequestDeferral? deferral =
                    null;

                try
                {
                    Console.WriteLine(
                        "DataRequested event received.");

                    deferral =
                        args.Request.GetDeferral();

                    /*
                     * Required by Windows Share.
                     */
                    args.Request
                        .Data
                        .Properties
                        .Title =
                            Path.GetFileName(
                                filePath);

                    /*
                     * Convert normal Windows path
                     * to StorageFile.
                     */
                    StorageFile storageFile =
                        await StorageFile
                            .GetFileFromPathAsync(
                                filePath);

                    /*
                     * Put the file into the
                     * Windows Share package.
                     */
                    args.Request
                        .Data
                        .SetStorageItems(
                            new List<IStorageItem>
                            {
                                storageFile
                            });

                    /*
                     * File sharing operation.
                     */
                    args.Request
                        .Data
                        .RequestedOperation =
                            DataPackageOperation.Copy;

                    Console.WriteLine(
                        "File added to Share package.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"DataRequested error: {ex}");

                    try
                    {
                        args.Request
                            .FailWithDisplayText(
                                "Unable to prepare the file for sharing.");
                    }
                    catch
                    {
                        // Ignore secondary error.
                    }
                }
                finally
                {
                    deferral?.Complete();
                }
            };

        /*
         * Subscribe BEFORE showing the Share UI.
         */
        dataTransferManager.DataRequested +=
            handler;

        try
        {
            Console.WriteLine(
                "Showing Windows Share UI...");

            /*
             * This associates the Share UI
             * with the JavaFX window.
             */
            interop.ShowShareUIForWindow(
                hwnd);

            /*
             * Keep the helper process alive.
             *
             * The Share UI is owned by Windows,
             * but the DataTransferManager/event
             * handler must remain alive.
             */
            await Task.Delay(
                TimeSpan.FromMinutes(5));
        }
        finally
        {
            /*
             * Always unsubscribe.
             */
            dataTransferManager.DataRequested -=
                handler;
        }
    }
}


/*
 * Windows DataTransferManager interop.
 *
 * IID:
 * 3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8
 */
[ComImport]
[Guid(
    "3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
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