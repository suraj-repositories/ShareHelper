using System;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length < 2) return;
        string filePath = args[0];
        if (!File.Exists(filePath)) return;
        IntPtr hwnd = (IntPtr)long.Parse(args[1]);

        IDataTransferManagerInterop interop = DataTransferManager.As<IDataTransferManagerInterop>();
        Guid iid = typeof(DataTransferManager).GUID;
        DataTransferManager dtm = DataTransferManager.FromAbi(interop.GetForWindow(hwnd, ref iid));

        dtm.DataRequested += (sender, e) =>
        {
            DataRequest request = e.Request;
            request.Data.Properties.Title = Path.GetFileName(filePath);
            var fileTask = StorageFile.GetFileFromPathAsync(filePath).AsTask();
            fileTask.Wait();
            request.Data.SetStorageItems(new[] { fileTask.Result });
        };

        interop.ShowShareUIForWindow(hwnd);
        await Task.Delay(2500);
    }
}

[System.Runtime.InteropServices.ComImport]
[System.Runtime.InteropServices.Guid("3A3DCD6C-3A07-43C1-982A-4167B640450B")]
[System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
interface IDataTransferManagerInterop
{
    IntPtr GetForWindow([System.Runtime.InteropServices.In] IntPtr appWindow, [System.Runtime.InteropServices.In] ref Guid riid);
    void ShowShareUIForWindow([System.Runtime.InteropServices.In] IntPtr appWindow);
}
