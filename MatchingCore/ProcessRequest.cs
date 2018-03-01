using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MatchingLib;
using LogHelper;
using System.Configuration;
using RabbitMQ.Client.Events;
using Newtonsoft.Json;
using System.Threading;

namespace MatchingCore
{
    internal class ProcessRequest
    {
        internal static ProcessRequest Instance { get; } = new ProcessRequest();
        BlockingCollection<RequestFromClient> RequestQueue { get; } = new BlockingCollection<RequestFromClient>();
        BlockingCollection<RequestFromClient> ResponseQueue { get; } = new BlockingCollection<RequestFromClient>();
        objPool<RequestFromClient> requestFcPools { get; } = new objPool<RequestFromClient>(() => new RequestFromClient());
        RabbitMqIn mqRequest { get; set; }
        RabbitMqOut mqOrderResponse { get; set; }
        RabbitMqOut mqTxResponse { get; set; }
        List<Task> tasksRunning { get; } = new List<Task>();

        internal void Shutdown()
        {
            Task.WaitAll(tasksRunning.ToArray());
        }

        internal void Init()
        {
            mqRequest = new RabbitMqIn(ConfigurationManager.AppSettings["RabbitMqRequestUri"].ToString(), ConfigurationManager.AppSettings["RabbitMqRequestQueueName"].ToString());
            mqRequest.BindReceived(MqInHandler);
            mqOrderResponse = new RabbitMqOut(ConfigurationManager.AppSettings["RabbitMqOrderResponseUri"].ToString(), ConfigurationManager.AppSettings["RabbitMqOrderResponseQueueName"].ToString());
            mqTxResponse = new RabbitMqOut(ConfigurationManager.AppSettings["RabbitMqTxResponseUri"].ToString(), ConfigurationManager.AppSettings["RabbitMqTxResponseQueueName"].ToString());
            tasksRunning.Add(Task.Factory.StartNew(() => HandleRequest(), TaskCreationOptions.LongRunning));
            for (int i = 0; i < 5; i++)
            {
                tasksRunning.Add(Task.Factory.StartNew(() => HandleResponse()));
            }
        }

        private void MqInHandler(object sender, BasicDeliverEventArgs ea)
        {
            var bytes = ea.Body;
            RequestFromClient request;
            if(!requestFcPools.CheckoutLimited(out request))
            {
                //reject incoming orders, since we hit the limit
                var reject = ProcessOrderResult.ConstructRejectBuffer(bytes);
                mqOrderResponse.Enqueue(reject.bytes);
                ProcessOrderResult.CheckIn(reject);
            }
            request.order = ProcessOrder.Instance.OrderPoolCheckOut();
            request.result.order = request.order;
            request.FromBytes(bytes);
            RequestQueue.Add(request);
            mqRequest.MsgFinished(ea);
        }

        internal void HandleResponse()
        {
            RequestFromClient request = null;
            while (!MatchingCoreSetup.Instance.cts.IsCancellationRequested)
            {
                try
                {
                    request = ResponseQueue.Take(MatchingCoreSetup.Instance.cts.Token);
                    #region send order response to order handling RabbitMQ
                    // Code here before result is being recycled
                    var resp = request.result.ToBytes();
                    mqOrderResponse.Enqueue(resp.bytes);
                    ProcessOrderResult.CheckIn(resp);
                    #endregion

                    #region send transaction to transaction handling RabbitMQ
                    //for (int i = 0; i < request.result.txList.Count; i++)
                    Parallel.For(0, request.result.txList.Count, i =>
                    {
                        BinaryObj response = request.result.txList[i].ToBytes();
                        mqTxResponse.Enqueue(response.bytes);
                        Transaction.CheckIn(response);
                    }
                    );
                    #endregion
                }
                catch (OperationCanceledException)
                {
                    LibraryLogger.Instance.WriteLog(LibraryLogger.libLogLevel.Info, "HandleResponse Thread shutting down");
                }
                catch (Exception ex)
                {
                    LibraryLogger.Instance.WriteLog(LibraryLogger.libLogLevel.Error, ex.ToString());
                }
                finally
                {
                    if (request != null)
                    {
                        if (request.result.CanRecycle)
                        {
                            request.order.ResetObj();
                            ProcessOrder.Instance.OrderPoolCheckIn(request.order);
                        }
                        foreach (var tx in request.result.txList)
                        {
                            tx.ResetObj();
                            ProcessOrder.Instance.TxPoolCheckIn(tx);
                        }
                        request.result.txList.Clear();
                        request.result.ResetObj();
                        requestFcPools.Checkin(request);
                    }
                }
            }
            LibraryLogger.Instance.WriteLog(LibraryLogger.libLogLevel.Info, string.Format("HandleResponse thread shutting down id:{0}", Thread.CurrentThread.ManagedThreadId));
        }

        internal void HandleRequest()
        {
            while (!MatchingCoreSetup.Instance.cts.IsCancellationRequested)
            {
                try
                {
                    foreach (var request in RequestQueue.GetConsumingEnumerable(MatchingCoreSetup.Instance.cts.Token))
                    {
                        if (request.type == RequestToMatching.RequestType.TradeOrder && (request.order.et == Order.ExecutionType.Limit || request.order.et == Order.ExecutionType.IoC))
                        {
                            ProcessOrder.Instance.IsOrderExist(request);
                            if (!request.result.Success)
                            {
                                request.result.ResetObj();
                                ProcessOrder.Instance.DoMatching(request);
                            }
                            else
                            {
                                request.result.Success = false;
                                request.result.errorType = ProcessOrderResult.ErrorType.OrderExists;
                            }
                        }
                        else if (request.type == RequestToMatching.RequestType.TradeOrder && request.order.et == Order.ExecutionType.AoN)
                        {
                            ProcessOrder.Instance.IsOrderExist(request);
                            if (request.result.Success)
                            {
                                request.result.Success = false;
                                request.result.errorType = ProcessOrderResult.ErrorType.OrderExists;
                            }
                            else
                            {
                                request.result.ResetObj();
                                ProcessOrder.Instance.CheckAllOrNothing(request);
                                if (request.result.Success)
                                {
                                    request.result.ResetObj();
                                    ProcessOrder.Instance.DoMatching(request);
                                }
                            }
                        }
                        else if (request.type == RequestToMatching.RequestType.IsOrderExist)
                        {
                            ProcessOrder.Instance.IsOrderExist(request);
                        }
                        else if (request.type == RequestToMatching.RequestType.CancelOrder)
                        {
                            ProcessOrder.Instance.DoCancel(request);
                        }
                        ResponseQueue.Add(request);
                    }
                }
                catch (OperationCanceledException)
                {
                    LibraryLogger.Instance.WriteLog(LibraryLogger.libLogLevel.Info, "HandleRequest Thread shutting down");
                }
                catch (Exception ex)
                {
                    LibraryLogger.Instance.WriteLog(LibraryLogger.libLogLevel.Error, ex.ToString());
                }
            }
        }
    }
}
