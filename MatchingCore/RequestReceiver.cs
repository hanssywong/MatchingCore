using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using BaseHelper;
using NLogHelper;
using MatchingLib;

namespace MatchingCore
{
    /// <summary>
    /// TCP Server
    /// </summary>
    public class RequestReceiver
    {
        /// <summary>
        /// Singleton
        /// </summary>
        public static RequestReceiver Server { get; } = new RequestReceiver();
        TcpListener listener { get; set; }
        public List<Task> ReceiverTasks { get; } = new List<Task>(10);
        public List<Task> SendTasks { get; } = new List<Task>(10);
        public List<TcpClient> ClientSockets { get; } = new List<TcpClient>(10);
        public SpinQueue<BinaryObj> respPool { get; } = new SpinQueue<BinaryObj>();
        public bool IsRunning { get { return bIsRunning; }private set { bIsRunning = value; } }
        volatile bool bIsRunning = true;
        const int ReceiveBufferSize = 512;
        objPool<byte[]> bufferPool { get; } = new objPool<byte[]>(() => new byte[ReceiveBufferSize]);

        public void SendResponse(IBinaryProcess binProc)
        {
            respPool.Enqueue(binProc.ToBytes());
        }
        public void StartListening(string ip, int port, int backlog = 1000)
        {
            // Establish the local endpoint for the socket.  
            listener = new TcpListener(IPAddress.Parse(ip), port);
            // Bind the socket to the local endpoint and listen for incoming connections.  
            try
            {
                listener.BeginAcceptTcpClient(AcceptCallback, listener);
                listener.Start(1000);
            }
            catch (Exception e)
            {
                NLogger.Instance.WriteLog(NLogger.LogLevel.Error, e.ToString());
            }
            NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "Tcp Server Listener started");
        }

        public void AcceptCallback(IAsyncResult ar)
        {
            // Get the socket that handles the client request.  
            //Socket listener = (Socket)ar.AsyncState;
            TcpClient client = listener.EndAcceptTcpClient(ar);
            ClientSockets.Add(client);
            ReceiverTasks.Add(Task.Factory.StartNew(() => ReceiverTask(client)));
            SendTasks.Add(Task.Factory.StartNew(() => SendTask(client)));
            listener.BeginAcceptTcpClient(AcceptCallback, listener);
        }

        private void SendTask(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            BinaryObj binObj = null;
            while (client.Connected)
            {
                try
                {
                    if (respPool.TryDequeue(out binObj))
                    {
                        // Read data from the client socket.   
                        stream.Write(binObj.bytes, 0, binObj.length);
                    }
                }
                catch (Exception e)
                {
                    NLogger.Instance.WriteLog(NLogger.LogLevel.Error, e.ToString());
                }
            }
        }

        private void ReceiverTask(TcpClient client)
        {
            var buffer = bufferPool.Checkout();
            NetworkStream stream = client.GetStream();
            while (client.Connected)
            {
                try
                {
                    var rfcObj = ProcessRequest.Instance.GetRfcObj();
                    // Read data from the client socket.   
                    int bytesRead = stream.Read(buffer, 0, 2);
                    int len = BitConverter.ToInt16(buffer, 0);
                    bytesRead = stream.Read(buffer, 2, len);

                    if (bytesRead > 0)
                    {
                        rfcObj.FromBytes(buffer);
                        ProcessRequest.Instance.ReceiveRequest(rfcObj);
                    }
                }
                catch (OperationCanceledException)
                {
                    NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "ReceiverTask shutdown");
                }
                catch (Exception e)
                {
                    NLogger.Instance.WriteLog(NLogger.LogLevel.Error, e.ToString());
                }
                finally
                {
                    if (client != null && !client.Connected)
                    {
                        IPEndPoint remoteIpEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
                        NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "Socket close on IP:" + remoteIpEndPoint?.Address + ", port:" + remoteIpEndPoint?.Port);
                        client.Close();
                        GC.SuppressFinalize(client);
                        client = null;
                        stream.Close();
                        GC.SuppressFinalize(stream);
                        bufferPool.Checkin(buffer);
                    }
                }
            }
        }

        /// <summary>
        /// Shutdown the listener, please call dispose before calling next new StartListening again
        /// </summary>
        public void Shutdown()
        {
            foreach(var client in ClientSockets)
            {
                if (client != null && client.Connected)
                {
                    client.Client.Shutdown(SocketShutdown.Both);
                }
            }
            Task.WaitAll(ReceiverTasks.ToArray());
            Task.WaitAll(SendTasks.ToArray());
            listener.Stop();
        }
    }
}
