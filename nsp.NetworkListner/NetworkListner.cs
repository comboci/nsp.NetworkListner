using System;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections;

namespace nsp.NetworkListner
{
    [DefaultEvent("DataRecived")]
    public partial class NetworkListner : Component
    {
        // Thread signal.
        public static ManualResetEvent allDone = new ManualResetEvent(false);
        Socket listener;

        public delegate void daRe(string IP, string Data);
        [Description("Rise when received a data from any client")]
        public event daRe DataRecived;

        public delegate void logEv(string Log);
        [Description("log a event from listener")]
        public event logEv LogRecived;

        public delegate void chngcount();
        [Description("fire when count of client change")]
        public event chngcount CountOfClinetChange;

        [Category("Nama")]
        [DefaultValue("0")]
        [Description("Port Of Server")]
        public string Port { get; set; }


        [Category("Nama")]
        [DefaultValue("127.0.0.1")]
        [Description("Ip of Server")]
        public string ServerIP { get; set; }

        private ArrayList OnlineSocket { get; set; }

        public NetworkListner()
        {
            InitializeComponent();
        }

        public NetworkListner(IContainer container)
        {
            container.Add(this);
            InitializeComponent();
        }

        public void StartListening(string IP, string port)
        {
            ServerIP = IP;
            Port = port;
            OnlineSocket = new ArrayList();

            if (bgw_StartListner == null)
            {
                bgw_StartListner = new System.ComponentModel.BackgroundWorker();
                bgw_StartListner.WorkerSupportsCancellation = true;
                bgw_StartListner.DoWork += new System.ComponentModel.DoWorkEventHandler(bgw_StartListner_DoWork);
            }

            bgw_StartListner.RunWorkerAsync();
        }

        private void bgw_StartListner_DoWork(object sender, DoWorkEventArgs e)
        {
            // Establish the local endpoint for the socket.
            // The DNS name of the computer
            // running the listener is "host.contoso.com".
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Parse(ServerIP), int.Parse(Port));

            // Create a TCP/IP socket.
            listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // Bind the socket to the local endpoint and listen for incoming connections.
            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(100);
                if (LogRecived != null)
                    LogRecived("Network Listener Was Started Successfully");

                while (true)
                {
                    // Set the event to non signaled state.
                    allDone.Reset();

                    // Start an asynchronous socket to listen for connections.
                    //Console.WriteLine("Waiting for a connection...");
                    if (LogRecived != null)
                        LogRecived("Network Listener Waiting for a connection...");

                    listener.BeginAccept(new AsyncCallback(AcceptCallback), listener);

                    // Wait until a connection is made before continuing.
                    allDone.WaitOne();
                }
            }
            catch (Exception ex)
            {
                if (LogRecived != null)
                    LogRecived(ex.ToString());
            }
        }

        public void AcceptCallback(IAsyncResult ar)
        {
            // Signal the main thread to continue.
            allDone.Set();

            if (listener == null) return;

            // Get the socket that handles the client request.
            Socket handler = listener.EndAccept(ar);

            if (CountOfClinetChange != null)
                CountOfClinetChange();

            if (LogRecived != null)
                LogRecived(string.Format("Client {0} is now connected", ((IPEndPoint)(handler.RemoteEndPoint)).Address.ToString()));

            if (!OnlineSocket.Contains(handler))
            {
                for (int i = 0; i < OnlineSocket.Count; i++)
                    if (OnlineSocket[i] != null)
                        if (((IPEndPoint)(((Socket)OnlineSocket[i]).RemoteEndPoint)).Address.ToString() == ((IPEndPoint)(handler.RemoteEndPoint)).Address.ToString())
                            OnlineSocket[i] = null;

                OnlineSocket.Add(handler);
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
                    LogRecived(string.Format("Client {0} is now disconected: ",
                        ((IPEndPoint)(handler.RemoteEndPoint)).Address.ToString()));
            }
            catch (Exception ex)
            {
                if (LogRecived != null)
                    LogRecived(ex.ToString());
            }

            if (bytesRead > 0)
            {
                // There  might be more data, so store the data received so far.
                //state.sb.Append(Encoding.ASCII.GetString(state.buffer, 0, bytesRead));
                //content = Encoding.Unicode.GetString(state.buffer, 0, bytesRead);

                for (int i = 0; i < bytesRead; i++)
                    content += (char)state.buffer[i];


                handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadCallback), state);

                if (DataRecived != null)
                    DataRecived(((IPEndPoint)(((StateObject)(ar.AsyncState)).workSocket.RemoteEndPoint)).Address.ToString(), content);
            }
        }

        public void Send(string IpAddress, string data)
        {
            for (int i = 0; i < OnlineSocket.Count; i++)
            {
                if (OnlineSocket[i] == null)
                    continue;

                if (((IPEndPoint)(((Socket)OnlineSocket[i]).RemoteEndPoint)).Address.ToString() == IpAddress)
                {
                    Send(((Socket)OnlineSocket[i]), data);
                    break;
                }
            }

            //foreach (Socket item in OnlineSocket)
            //    if (((System.Net.IPEndPoint)(item.RemoteEndPoint)).Address.ToString() == IpAddress)
            //    {
            //        Send(item, data);
            //        break;
            //    }
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

                if (LogRecived != null)
                    LogRecived(string.Format("Sent {0} bytes to client.", bytesSent));

                //handler.Shutdown(SocketShutdown.Both);
                //handler.Close();
            }
            catch (Exception e)
            {
                if (LogRecived != null)
                    LogRecived(e.ToString());
            }
        }

        public void StopListening()
        {
            listener.Close();
            listener = null;
            bgw_StartListner = null;

            if (CountOfClinetChange != null)
                CountOfClinetChange();

            if (LogRecived != null)
                LogRecived("Server stopped");
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
