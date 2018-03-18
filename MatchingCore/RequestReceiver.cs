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
    /// Request Receiver
    /// </summary>
    internal class RequestReceiver
    {
        /// <summary>
        /// Singleton
        /// </summary>
        internal static RequestReceiver Server { get; } = new RequestReceiver();
        TcpListener listener { get; set; }
        private Task AcceptTask { get; set; }
        private List<Task> ReceiverTasks { get; } = new List<Task>(10);
        private List<Task> SendTasks { get; } = new List<Task>(10);
        private List<TcpClient> ClientSockets { get; } = new List<TcpClient>(10);
        private SpinQueue<BinaryObj> respQueue { get; } = new SpinQueue<BinaryObj>();
        private int ReceiveBufferSize { get; } = 512;

        internal void SendResponse(IBinaryProcess binProc)
        {
            respQueue.Enqueue(binProc.ToBytes());
        }
        internal void StartListening(string ip, int port, int backlog = 1000)
        {
            // Establish the local endpoint for the socket.  
            listener = new TcpListener(IPAddress.Parse(ip), port);
            // Bind the socket to the local endpoint and listen for incoming connections.  
            try
            {
                listener.Start(backlog);
                AcceptTask = Task.Factory.StartNew(() => AcceptClientTask());
                //listener.BeginAcceptTcpClient(AcceptCallback, listener);
            }
            catch (Exception e)
            {
                NLogger.Instance.WriteLog(NLogger.LogLevel.Error, e.ToString());
            }
            NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "RequestReceiver Listener started");
        }

        //private void AcceptCallback(IAsyncResult ar)
        private void AcceptClientTask()
        {
            while (!MatchingCoreSetup.Instance.cts.IsCancellationRequested)
            {
                // Get the socket that handles the client request.  
                //Socket listener = (Socket)ar.AsyncState;
                TcpClient client = listener.AcceptTcpClient();//.EndAcceptTcpClient(ar);
                ClientSockets.Add(client);
                ReceiverTasks.Add(Task.Factory.StartNew(() => ReceiverTask(client)));
                SendTasks.Add(Task.Factory.StartNew(() => SendTask(client)));
                //listener.BeginAcceptTcpClient(AcceptCallback, listener);
                IPEndPoint remoteIpEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
                NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "AcceptClientTask new client connected from IP:" + remoteIpEndPoint?.Address + ", port:" + remoteIpEndPoint?.Port);
            }
            NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "AcceptClientTask shutdown");
        }

        private void SendTask(TcpClient client)
        {
            NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "RequestReceiver SendTask start");
            NetworkStream stream = client.GetStream();
            BinaryObj binObj = null;
            while (client != null && client.Connected)
            {
                try
                {
                    if (respQueue.TryDequeue(out binObj))
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
            NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "RequestReceiver SendTask shutdown");
        }

        private void ReceiverTask(TcpClient client)
        {
            NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "RequestReceiver ReceiverTask start");
            var prevbuff = new byte[ReceiveBufferSize];
            var buffer = new byte[ReceiveBufferSize];
            var errorbuffer = new byte[1024*1024];
            NetworkStream stream = client.GetStream();
            while (client != null && client.Connected)
            {
                RequestFromClient rfcObj = null;
                int bytesRead = 0;
                int len = 0;
                try
                {
                    rfcObj = ProcessRequest.Instance.GetRfcObj();
                    // Read data from the client socket.   
                    bytesRead = stream.Read(buffer, 0, 2);

                    if (bytesRead > 0)
                    {
                        bytesRead = 0;
                        len = BitConverter.ToInt16(buffer, 0);
                        if (len > ReceiveBufferSize)
                        {
                            NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "len:" + len);
                            Array.Copy(buffer, errorbuffer, 2);
                            bytesRead = stream.Read(errorbuffer, 2, len - 2);
                            NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "errorbuffer:" + BitConverter.ToString(errorbuffer, 0, len));
                            continue;
                        }
                        else
                        {
                            bytesRead = stream.Read(buffer, 2, len - 2);
                        }
                        rfcObj.FromBytes(buffer);
                        ProcessRequest.Instance.ReceiveRequest(rfcObj);
                    }
                    else
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
                    NLogger.Instance.WriteLog(NLogger.LogLevel.Error, "buffer:" + BitConverter.ToString(buffer, 0, len));
                    NLogger.Instance.WriteLog(NLogger.LogLevel.Error, e.ToString());
                }
                finally
                {
                    if (client != null && !client.Connected)
                    {
                        ClientSockets.Remove(client);
                        IPEndPoint remoteIpEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
                        NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "RequestReceiver socket closed from IP:" + remoteIpEndPoint?.Address + ", port:" + remoteIpEndPoint?.Port);
                        client.Close();
                        GC.SuppressFinalize(client);
                        client = null;
                        stream.Close();
                        GC.SuppressFinalize(stream);
                        respQueue.ManualFreeBlocking();
                    }
                    //Array.Clear(prevbuff, 0, prevbuff.Length);
                    //Array.Copy(buffer, prevbuff, buffer.Length);
                    //Array.Clear(buffer, 0, buffer.Length);
                    //Array.Clear(errorbuffer, 0, errorbuffer.Length);
                }
            }
            NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "RequestReceiver ReceiverTask shutdown");
        }

        /// <summary>
        /// Shutdown the listener, please call dispose before calling next new StartListening again
        /// </summary>
        internal void Shutdown()
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
            NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "RequestReceiver shutdown");
        }
    }
}
