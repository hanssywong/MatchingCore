﻿using System;
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
                listener.BeginAcceptTcpClient(AcceptCallback, listener);
                listener.Start(backlog);
            }
            catch (Exception e)
            {
                NLogger.Instance.WriteLog(NLogger.LogLevel.Error, e.ToString());
            }
            NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "RequestReceiver Listener started");
        }

        private void AcceptCallback(IAsyncResult ar)
        {
            // Get the socket that handles the client request.  
            //Socket listener = (Socket)ar.AsyncState;
            TcpClient client = listener.EndAcceptTcpClient(ar);
            ClientSockets.Add(client);
            ReceiverTasks.Add(Task.Factory.StartNew(() => ReceiverTask(client)));
            SendTasks.Add(Task.Factory.StartNew(() => SendTask(client)));
            listener.BeginAcceptTcpClient(AcceptCallback, listener);
            IPEndPoint remoteIpEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
            NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "RequestReceiver new client connected from IP:" + remoteIpEndPoint?.Address + ", port:" + remoteIpEndPoint?.Port);
        }

        private void SendTask(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            BinaryObj binObj = null;
            while (client.Connected)
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
            var buffer = new byte[ReceiveBufferSize];
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
                        NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "RequestReceiver socket closed from IP:" + remoteIpEndPoint?.Address + ", port:" + remoteIpEndPoint?.Port);
                        client.Close();
                        GC.SuppressFinalize(client);
                        client = null;
                        stream.Close();
                        GC.SuppressFinalize(stream);
                    }
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
