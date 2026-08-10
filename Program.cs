using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinRT.Interop;

class Program
{
    [STAThread]
    static async Task Main(string[] args)
    {
        if (args.Length < 2) return;

        string filePath = args[0];
        if (!File.Exists(filePath)) return;

        if (!long.TryParse(args[1], out long hwndLong)) return;
        IntPtr hwnd = new IntPtr(hwndLong);

        try
        {
            // 1. Get the interop instance using the system COM interface
            IDataTransferManagerInterop interop = DataTransferManagerInterop.GetNative();

            // 2. Fetch the DataTransferManager specifically bound to your JavaFX HWND
            Guid dtmIid = typeof(DataTransferManager).GUID;
            IntPtr dtmPtr = interop.GetForWindow(hwnd, ref dtmIid);
            DataTransferManager dtm = DataTransferManager.FromAbi(dtmPtr);

            // 3. Register DataRequested callback
            dtm.DataRequested += async (sender, e) =>
            {
                DataRequestDeferral deferral = e.Request.GetDeferral();
                try
                {
                    e.Request.Data.Properties.Title = Path.GetFileName(filePath);
                    
                    StorageFile storageFile = await StorageFile.GetFileFromPathAsync(filePath);
                    e.Request.Data.SetStorageItems(new List<IStorageItem> { storageFile });
                }
                finally
                {
                    deferral.Complete();
                }
            };

            // 4. Trigger Windows Share UI anchored to the JavaFX Window Handle
            interop.ShowShareUIForWindow(hwnd);

            // Give Windows WinRT process time to process the thread transfer
            await Task.Delay(3000);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Share UI Error: {ex.Message}");
        }
    }
}

// Fixed COM Interface Definition & Factory
[ComImport]
[Guid("3A3DCD28-0057-4D77-9A9A-10B39774002D")] // Correct Win32 IID
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IDataTransferManagerInterop
{
    IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
    void ShowShareUIForWindow([In] IntPtr appWindow);
}

static class DataTransferManagerInterop
{
    [DllImport("api-ms-win-core-winrt-l1-1-0.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RoGetActivationFactory(
        [MarshalAs(UnmanagedType.BStr)] string runtimeClassId,
        [In] ref Guid riid,
        out IntPtr factory);

    public static IDataTransferManagerInterop GetNative()
    {
        Guid interopGuid = typeof(IDataTransferManagerInterop).GUID;
        int hr = RoGetActivationFactory("Windows.ApplicationModel.DataTransfer.DataTransferManager", ref interopGuid, out IntPtr factory);
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        return (IDataTransferManagerInterop)Marshal.GetObjectForIUnknown(factory);
    }
}