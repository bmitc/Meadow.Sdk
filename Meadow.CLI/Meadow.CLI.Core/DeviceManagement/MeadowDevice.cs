﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Threading.Tasks;
using MeadowCLI.Hcom;
using System.Linq;

namespace MeadowCLI.DeviceManagement
{
    public class MeadowDeviceException : Exception
    {
        public MeadowDeviceException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    //a simple model object that represents a meadow device including connection
    public class MeadowDevice
    {
        const string MSCORLIB = "mscorlib.dll";
        const string SYSTEM = "System.dll";
        const string SYSTEM_CORE = "System.Core.dll";
        const string MEADOW_CORE = "Meadow.Core.dll";
        const string APP_EXE = "App.exe";

        public SerialPort SerialPort { get; private set; }

        public string Name { get; private set; } = "Meadow Micro F7";

        public string Model { get; private set; } = "Micro F7";
        
        public string Id { get; set; } //guessing we'll need this

        private MeadowSerialDataProcessor dataProcessor;

        private string serialPortName;

        private List<string> filesOnDevice = new List<string>();

        public MeadowDevice(string serialPortName, string deviceName = null)
        {
            if(string.IsNullOrWhiteSpace(deviceName) == false)
                Name = deviceName; //otherwise use the default

            Id = Guid.NewGuid().ToString();

            this.serialPortName = serialPortName;
        }

        public void Initialize (bool listen = true)
        {
            if(SerialPort != null)
            {
                SerialPort.Close();
                SerialPort = null;
            }

            OpenSerialPort();

            if(listen == true)
                ListenForSerialData();
        }

        public async Task DeployRequiredLibs(string path, bool forceUpdate = false)
        {
            if(forceUpdate || await IsFileOnDevice(SYSTEM).ConfigureAwait(false) == false)
            {
                await WriteFile(SYSTEM, path).ConfigureAwait(false);
            }

            if (forceUpdate || await IsFileOnDevice(SYSTEM_CORE).ConfigureAwait(false) == false)
            {
                await WriteFile(SYSTEM_CORE, path).ConfigureAwait(false);
            }

            if (forceUpdate || await IsFileOnDevice(MSCORLIB).ConfigureAwait(false) == false)
            {
                await WriteFile(MSCORLIB, path).ConfigureAwait(false);
            }

            if (forceUpdate || await IsFileOnDevice(MEADOW_CORE).ConfigureAwait(false) == false)
            {
                await WriteFile(MEADOW_CORE, path).ConfigureAwait(false);
            }
        }

        public async Task<bool> DeployApp(string path)
        {
            await WriteFile(APP_EXE, path);

            //get list of files in folder
            var files = Directory.GetFiles(path, "*.dll");

            //currently deploys all included dlls, update to use CRCs to only deploy new files
            //will likely need to update to deploy other files types (txt, jpg, etc.)
            foreach(var f in files)
            {
                var file = Path.GetFileName(f);
                if (file == MSCORLIB || file == SYSTEM || file == SYSTEM_CORE || file == MEADOW_CORE)
                    continue;

                await WriteFile(file, path);
            }

            return true; //can probably remove bool return type
        }

        public async Task<bool> WriteFile(string filename, string path, int timeoutInMs = 200000) //200s 
        {
            if (SerialPort == null)
            {
                throw new Exception("SerialPort not intialized");
            }

            bool result = false;

            var timeOutTask = Task.Delay(timeoutInMs);

            EventHandler<MeadowMessageEventArgs> handler = null;

            var tcs = new TaskCompletionSource<bool>();

            handler = (s, e) =>
            {
                if (e.Message.Contains("File Sent Successfully"))
                {
                    result = true;
                    tcs.SetResult(true);
                }
            };
            dataProcessor.OnReceiveData += handler;

            MeadowFileManager.WriteFileToFlash(this, Path.Combine(path, filename), filename);

            await Task.WhenAny(new Task[] { timeOutTask, tcs.Task });
            dataProcessor.OnReceiveData -= handler;

            return result;
        }

        public async Task<List<string>> GetFilesOnDevice(bool refresh = false, int timeoutInMs = 10000)
        {
            if (SerialPort == null)
            {
                throw new Exception("SerialPort not intialized");
            }

            if(filesOnDevice.Count == 0 || refresh == true)
            {
                var timeOutTask = Task.Delay(timeoutInMs);

                EventHandler<MeadowMessageEventArgs> handler = null;

                var tcs = new TaskCompletionSource<bool>();

                handler = (s, e) =>
                {
                    if(e.MessageType == MeadowMessageType.FileList)
                    {
                        SetFilesOnDeviceFromMessage(e.Message);
                        tcs.SetResult(true);
                    }
                };
                dataProcessor.OnReceiveData += handler;

                MeadowFileManager.ListFiles(this);

                await Task.WhenAny(new Task[] { timeOutTask, tcs.Task});
                dataProcessor.OnReceiveData -= handler;
            }

            return filesOnDevice;
        }

        public Task<bool> IsFileOnDevice (string filename)
        {
            return Task.FromResult(filesOnDevice.Contains(filename));
        }

        public async Task<string> GetDeviceInfo(int timeoutInMs = 500)
        {
            var timeOutTask = Task.Delay(timeoutInMs);
            var deviceInfo = string.Empty;

            EventHandler<MeadowMessageEventArgs> handler = null;

            var tcs = new TaskCompletionSource<bool>();

            handler = (s, e) =>
            {
                if (e.MessageType == MeadowMessageType.DeviceInfo)
                {
                    deviceInfo = e.Message;
                    tcs.SetResult(true);
                }
            };
            dataProcessor.OnReceiveData += handler;

            MeadowDeviceManager.GetDeviceInfo(this);

            await Task.WhenAny(new Task[] { timeOutTask, tcs.Task });
            dataProcessor.OnReceiveData -= handler;

            return deviceInfo;
        }

        //putting this here for now ..... 
        void OpenSerialPort()
        {
            try
            {   // Create a new SerialPort object with default settings
                var port = new SerialPort
                {
                    PortName = serialPortName,
                    BaudRate = 115200,       // This value is ignored when using ACM
                    Parity = Parity.None,
                    DataBits = 8,
                    StopBits = StopBits.One,
                    Handshake = Handshake.None,

                    // Set the read/write timeouts
                    ReadTimeout = 5000,
                    WriteTimeout = 5000
                };

                port.Open();

                //improves perf on Windows?
                port.BaseStream.ReadTimeout = 0;

                SerialPort = port;
            }
            catch (IOException ioEx)
            {
                throw new MeadowDeviceException($"The specified port '{serialPortName}' could not be found or opened.", ioEx);
            }
            catch (Exception ex)
            {
                throw new MeadowDeviceException($"Unknown exception", ex);
            }
        }

        internal void ListenForSerialData()
        {
            if (SerialPort != null)
            {
                dataProcessor = new MeadowSerialDataProcessor(SerialPort);

                dataProcessor.OnReceiveData += DataReceived;
            }
        }

        void DataReceived (object sender, MeadowMessageEventArgs args)
        {
            switch(args.MessageType)
            {
                case MeadowMessageType.Data:
                    Console.Write("Data: " + args.Message);
                    break;
                case MeadowMessageType.AppOutput:
                    Console.Write("App: " + args.Message);
                    break;
                case MeadowMessageType.FileList:
                    SetFilesOnDeviceFromMessage(args.Message);
                    Console.WriteLine();
                    foreach (var f in filesOnDevice)
                        Console.WriteLine(f);
                    break;
                case MeadowMessageType.DeviceInfo:
                    SetDeviceIdFromMessage(args.Message);
                    Console.WriteLine("ID: " + args.Message);
                    break;
            }
        }

        void SetFilesOnDeviceFromMessage(string message)
        {
            var fileList = message.Split(',');

            filesOnDevice.Clear();

            foreach (var path in fileList)
            {
                var file = path.Substring(path.LastIndexOf('/') + 1);
                filesOnDevice.Add(file);
            }
        }

        void SetDeviceIdFromMessage(string message)
        {

        }
    }
}