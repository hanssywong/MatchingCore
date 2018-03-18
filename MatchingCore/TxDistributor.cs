using BaseHelper;
using MatchingLib;
using NLogHelper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace MatchingCore
{
    public class TxDistributor
    {
        /// <summary>
        /// Singleton
        /// </summary>
        public static TxDistributor Server { get; } = new TxDistributor();
        private TcpListener listener { get; set; }
        private List<Task> SenderTask { get; } = new List<Task>();
        SpinQueue<BinaryObj> binObjQueue { get; } = new SpinQueue<BinaryObj>();
        private List<Task> ReceiverTasks { get; } = new List<Task>(10);
        private List<Task> SendTasks { get; } = new List<Task>(10);
        private List<TcpClient> ClientSockets { get; } = new List<TcpClient>(10);
        private int ReceiveBufferSize { get; } = 512;

        internal void SendResponse(IBinaryProcess binProc)
        {
            binObjQueue.Enqueue(binProc.ToBytes());
        }
        internal void StartListening(string ip, int port, int backlog = 1000)
        {
            // Establish the local endpoint for the socket.  
            listener = new TcpListener(IPAddress.Parse(ip), port);
            // Bind the socket to the local endpoint and listen for incoming connections.  
            try
            {
                listener.Start(backlog);
                listener.BeginAcceptTcpClient(AcceptCallback, listener);
            }
            catch (Exception e)
            {
                NLogger.Instance.WriteLog(NLogger.LogLevel.Error, e.ToString());
            }
            NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "TxDistributor Listener started");
        }

        private void AcceptCallback(IAsyncResult ar)
        {
            // Get the socket that handles the client request.  
            //Socket listener = (Socket)ar.AsyncState;
            TcpClient client = listener.EndAcceptTcpClient(ar);
            client.NoDelay = true;
            ClientSockets.Add(client);
            ReceiverTasks.Add(Task.Factory.StartNew(() => ReceiverTask(client)));
            SendTasks.Add(Task.Factory.StartNew(() => SendTask(client)));
            listener.BeginAcceptTcpClient(AcceptCallback, listener);
            IPEndPoint remoteIpEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
            NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "TxDistributor new client connected from IP:" + remoteIpEndPoint?.Address + ", port:" + remoteIpEndPoint?.Port);
        }

        private void SendTask(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            while (client != null && client.Connected)
            {
                BinaryObj binObj = null;
                try
                {
                    if (binObjQueue.TryDequeue(out binObj))
                    {
                        // Read data from the client socket.   
                        stream.Write(binObj.bytes, 0, binObj.length);
                        stream.Flush();
                    }
                }
                catch (Exception e)
                {
                    NLogger.Instance.WriteLog(NLogger.LogLevel.Error, e.ToString());
                }
                finally
                {
                    if (binObj != null)
                    {
                        binObj.ResetOjb();
                        BinaryObjPool.Checkin(binObj);
                    }
                }
            }
            NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "TxDistributor SendTask shutdown");
        }

        private void ReceiverTask(TcpClient client)
        {
            var buffer = new byte[ReceiveBufferSize];
            NetworkStream stream = client.GetStream();
            while (client != null && client.Connected)
            {
                try
                {
                    // Read data from the client socket.   
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead <= 0)
                    {
                        if (client.Connected)
                            client.Client.Shutdown(SocketShutdown.Send);
                    }
                }
                catch (OperationCanceledException)
                {
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
                        NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "TxDistributor socket closed from IP:" + remoteIpEndPoint?.Address + ", port:" + remoteIpEndPoint?.Port);
                        client.Close();
                        GC.SuppressFinalize(client);
                        client = null;
                        stream.Close();
                        GC.SuppressFinalize(stream);
                        stream = null;
                        binObjQueue.ReleaseBlocking();
                    }
                }
            }
            NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "TxDistributor ReceiverTask shutdown");
        }

        /// <summary>
        /// Shutdown the listener, please call dispose before calling next new StartListening again
        /// </summary>
        internal void Shutdown()
        {
            foreach (var client in ClientSockets)
            {
                if (client != null && client.Connected)
                {
                    client.Client.Shutdown(SocketShutdown.Both);
                }
            }
            Task.WaitAll(ReceiverTasks.ToArray());
            Task.WaitAll(SendTasks.ToArray());
            listener.Stop();
            NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "TxDistributor shutdown");
        }
    }
}
