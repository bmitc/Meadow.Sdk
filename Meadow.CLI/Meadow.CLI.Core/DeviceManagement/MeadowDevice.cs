﻿using System;
using System.IO;
using System.IO.Ports;

namespace MeadowCLI.DeviceManagement
{
    //a simple model object that represents meadow
    public class MeadowDevice
    {
        public SerialPort SerialPort { get; set; }

        public string Name { get; private set; } = "Meadow Mirco F7";

        public string Model { get; private set; }
        
        
        public string Id { get; set; } //guessing we'll need this

        public MeadowDevice(string serialPortName, string deviceName = null)
        {
            if(string.IsNullOrWhiteSpace(deviceName) == false)
                Name = deviceName;

            OpenSerialPort(serialPortName);
        }

        //putting this here for now .....
        public bool OpenSerialPort(string portName)
        {
            try
            {
                // Create a new SerialPort object with default settings.
                SerialPort = new SerialPort
                {
                    PortName = portName,
                    BaudRate = 115200,       // This value is ignored when using ACM
                    Parity = Parity.None,
                    DataBits = 8,
                    StopBits = StopBits.One,
                    Handshake = Handshake.None,

                    // Set the read/write timeouts
                    ReadTimeout = 500,
                    WriteTimeout = 500
                };

                SerialPort.Open();
                Console.WriteLine("Port: {0} opened", portName);
                return true;
            }
            catch (IOException ioe)
            {
                Console.WriteLine("The specified port '{0}' could not be found or opened. {1}Exception:'{2}'",
                    portName, Environment.NewLine, ioe);
                throw;
            }
            catch (Exception except)
            {
                Console.WriteLine("Unknown exception:{0}", except);
                throw;
            }
        }
    }
}