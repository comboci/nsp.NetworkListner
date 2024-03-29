﻿using System;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.ComponentModel;
using System.Collections.Generic;

namespace nsp.NetworkListner
{
    [DefaultEvent("DataRecived")]
    public partial class NetworkListner : Component
    {
        public enum LogTypes
        {
            SuccessfulInitial,
            UnsuccessfulInitial,
            WaitingForConnection,
            ClientConnected,
            ClientDisconnected,
            DateSent,
            ServerStoped,
            Error
        }

        // Thread signal.
        public static ManualResetEvent allDone = new ManualResetEvent(false);
        Socket listener;

        public delegate void dataRecived(string IP, string Data);
        [Description("Rise when received a data from any client")]
        public event dataRecived DataRecived;

        public delegate void logRecived(LogTypes LogType, string Log);
        [Description("Log a event from listener")]
        public event logRecived LogRecived;

        public delegate void onClientConnect(string IpAddress);
        [Description("Connect a Client To Listner")]
        public event onClientConnect OnClientConnect;

        public delegate void countOfClinetChange();
        [Description("Fire when count of client change")]
        public event countOfClinetChange CountOfClinetChange;

        public delegate void onClientDisConnect(string IpAddress);
        [Description("DisConnect a Client To Listner")]
        public event onClientDisConnect OnClientDisConnect;

        [Category("nsp")]
        [DefaultValue("0")]
        [Description("Port Of Server")]
        public string Port { get; set; }


        [Category("nsp")]
        [DefaultValue("127.0.0.1")]
        [Description("Ip of Server")]
        public string ServerIP { get; set; }

        public List<Socket> OnlineSocket;

        public NetworkListner()
        {
            InitializeComponent();
            if (LicenseManager.UsageMode != LicenseUsageMode.Designtime)//detect design mode
                OnlineSocket = new List<Socket>();
        }

        public NetworkListner(IContainer container)
        {
            container.Add(this);
            InitializeComponent();
            if (LicenseManager.UsageMode != LicenseUsageMode.Designtime)//detect design mode
                OnlineSocket = new List<Socket>();
        }

        public void StartListening(string IP, string port)
        {
            ServerIP = IP;
            Port = port;

            if (bgw_StartListner == null)
            {
                bgw_StartListner = new BackgroundWorker();
                bgw_StartListner.WorkerSupportsCancellation = true;
                bgw_StartListner.DoWork += new DoWorkEventHandler(bgw_StartListner_DoWork);
            }

            bgw_StartListner.RunWorkerAsync();
        }

        private void bgw_StartListner_DoWork(object sender, DoWorkEventArgs e)
        {
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Parse(ServerIP), int.Parse(Port));
            listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(100);
                LogRecived?.Invoke(LogTypes.SuccessfulInitial, "Network Listener Was Started Successfully");

                while (true)
                {
                    allDone.Reset();
                    LogRecived?.Invoke(LogTypes.WaitingForConnection, "Network Listener Waiting for a connection...");
                    listener.BeginAccept(new AsyncCallback(AcceptCallback), listener);
                    allDone.WaitOne();
                }
            }
            catch (Exception ex)
            {
                LogRecived?.Invoke(LogTypes.UnsuccessfulInitial, ex.ToString());
            }
        }

        public void AcceptCallback(IAsyncResult ar)
        {
            allDone.Set();

            if (OnlineSocket == null)
                OnlineSocket = new List<Socket>();

            if (listener == null) return;

            Socket handler = listener.EndAccept(ar);

            CountOfClinetChange?.Invoke();

            LogRecived?.Invoke(LogTypes.ClientConnected, string.Format("Client {0} is now connected", handler.IpAdress()));

            if (!OnlineSocket.Contains(handler))
            {
                //OnlineSocket.ForEach(x =>
                //{
                //    if (x!=null && x.IpAdress() == handler.IpAdress())
                //        x = null;
                //});

                for (int i = 0; i < OnlineSocket.Count; i++)
                    if (OnlineSocket[i] != null)
                        if (OnlineSocket[i].IpAdress() == handler.IpAdress())
                            OnlineSocket[i] = null;

                OnlineSocket.Add(handler);
                OnClientConnect?.Invoke(handler.IpAdress());
            }

            // Create the state object.
            StateObject state = new StateObject();
            state.workSocket = handler;
            handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadCallback), state);
        }

        public void ReadCallback(IAsyncResult ar)
        {
            string content = string.Empty;

            // Retrieve the state object and the handler socket
            // from the asynchronous state object.
            StateObject state = (StateObject)ar.AsyncState;
            Socket handler = state.workSocket;
            int bytesRead = 0;
            try
            {
                if (!((handler.Poll(1000, SelectMode.SelectRead) && (handler.Available == 0)) || !handler.Connected))
                    // Read data from the client socket. 
                    bytesRead = handler.EndReceive(ar);
                else
                {
                    LogRecived?.Invoke(LogTypes.ClientDisconnected, string.Format("Client {0} is now disconected: ", handler.IpAdress()));
                    OnClientDisConnect?.Invoke(handler.IpAdress());
                }
            }
            catch (Exception ex)
            {
                LogRecived?.Invoke(LogTypes.Error, ex.ToString());
            }

            if (bytesRead > 0)
            {
                // There  might be more data, so store the data received so far.
                //state.sb.Append(Encoding.ASCII.GetString(state.buffer, 0, bytesRead));
                //content = Encoding.Unicode.GetString(state.buffer, 0, bytesRead);

                for (int i = 0; i < bytesRead; i++)
                    content += (char)state.buffer[i];

                handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadCallback), state);

                DataRecived?.Invoke(((IPEndPoint)(((StateObject)(ar.AsyncState)).workSocket.RemoteEndPoint)).Address.ToString(), content);
            }
        }

        public void Send(string IpAddress, string data)
        {
            if (OnlineSocket == null)
            {
                LogRecived?.Invoke(LogTypes.Error, "OnlineSocket is null");
                return;
            }

            for (int i = 0; i < OnlineSocket.Count; i++)
            {
                if (OnlineSocket[i] == null)
                    continue;

                if (OnlineSocket[i].IpAdress() == IpAddress)
                {
                    Send(OnlineSocket[i], data);
                    break;
                }
            }
        }

        private void Send(Socket handler, string data)
        {
            // Convert the string data to byte data using ASCII encoding.
            byte[] byteData = Encoding.ASCII.GetBytes(data);

            // Begin sending the data to the remote device.
            handler.BeginSend(byteData, 0, byteData.Length, 0, new AsyncCallback(SendCallback), handler);
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket handler = (Socket)ar.AsyncState;

                // Complete sending the data to the remote device.
                int bytesSent = handler.EndSend(ar);

                LogRecived?.Invoke(LogTypes.DateSent, string.Format("Sent {0} bytes to client.", bytesSent));

                //handler.Shutdown(SocketShutdown.Both);
                //handler.Close();
            }
            catch (Exception e)
            {
                LogRecived?.Invoke(LogTypes.Error, e.ToString());
            }
        }

        public void StopListening()
        {
            listener.Close();
            listener = null;
            bgw_StartListner = null;

            CountOfClinetChange?.Invoke();

            LogRecived?.Invoke(LogTypes.ServerStoped, "Server stopped");
        }
    }

    public static class Helper
    {
        public static string IpAdress(this Socket socket)
        {
            if (socket == null)
                return "";
            return ((IPEndPoint)socket.RemoteEndPoint).Address.ToString();
        }
    }

    public class StateObject
    {
        // Client  socket.
        public Socket workSocket = null;
        // Size of receive buffer.
        public const int BufferSize = 1024;
        // Receive buffer.
        public byte[] buffer = new byte[BufferSize];
        // Received data string.
        public StringBuilder sb = new StringBuilder();
    }
}
