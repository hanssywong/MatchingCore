using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MatchingLib;
using System.Configuration;
using RabbitMQ.Client.Events;
using Newtonsoft.Json;
using System.Threading;
using NLogHelper;
using BaseHelper;

namespace MatchingCore
{
    internal class ProcessRequest
    {
        internal static ProcessRequest Instance { get; } = new ProcessRequest();
        SpinQueue<RequestFromClient> RequestQueue { get; } = new SpinQueue<RequestFromClient>();
        SpinQueue<RequestFromClient> ResponseQueue { get; } = new SpinQueue<RequestFromClient>();
        objPoolV2<RequestFromClient> requestFcPools { get; } = new objPoolV2<RequestFromClient>(() => new RequestFromClient(), 5000);
        //RabbitMqIn mqRequest { get; set; }
        //RabbitMqOut mqOrderResponse { get; set; }
        //RabbitMqOut mqTxResponse { get; set; }
        List<Task> tasksRunning { get; } = new List<Task>();
        int mqInCnt = 0;
        int mqRejCnt = 0;
        /// <summary>
        /// Request per second
        /// </summary>
        int Rps = 0;
        /// <summary>
        /// Tx per second
        /// </summary>
        //long Tps = 0;
        /// <summary>
        /// Rejected per second
        /// </summary>
        //long Rejps = 0;

        internal void Shutdown()
        {
            requestFcPools.Shutdown();
            RequestQueue.ShutdownGracefully();
            ResponseQueue.ShutdownGracefully();
            //mqRequest.Shutdown();
            //mqOrderResponse.Shutdown();
            //mqTxResponse.Shutdown();
            Task.WaitAll(tasksRunning.ToArray());
        }

        internal void Init()
        {
            //ushort prefetchCount = ushort.Parse(ConfigurationManager.AppSettings["prefetchCount"]);
            //mqRequest = new RabbitMqIn(ConfigurationManager.AppSettings["RabbitMqRequestUri"].ToString(), ConfigurationManager.AppSettings["RabbitMqRequestQueueName"].ToString(), prefetchCount);
            //mqRequest.BindReceived(MqInHandler);
            //mqOrderResponse = new RabbitMqOut(ConfigurationManager.AppSettings["RabbitMqOrderResponseUri"].ToString(), ConfigurationManager.AppSettings["RabbitMqOrderResponseQueueName"].ToString());
            //mqTxResponse = new RabbitMqOut(ConfigurationManager.AppSettings["RabbitMqTxResponseUri"].ToString(), ConfigurationManager.AppSettings["RabbitMqTxResponseQueueName"].ToString());
            RequestReceiver.Server.StartListening(ConfigurationManager.AppSettings["RequestReceiverIP"].ToString(), int.Parse(ConfigurationManager.AppSettings["RequestReceiverPort"].ToString()));
            TxDistributor.Server.StartListening(ConfigurationManager.AppSettings["TxReceiverIP"].ToString(), int.Parse(ConfigurationManager.AppSettings["TxReceiverPort"].ToString()));
            tasksRunning.Add(Task.Factory.StartNew(() => HandleRequest(), TaskCreationOptions.LongRunning));
            for (int i = 0; i < 1; i++)
            {
                tasksRunning.Add(Task.Factory.StartNew(() => HandleResponse(), TaskCreationOptions.LongRunning));
            }
        }

        internal void ReceiveRequest(RequestFromClient rfcObj)
        {
            RequestQueue.Enqueue(rfcObj);
            Rps++;
        }

        internal RequestFromClient GetRfcObj()
        {
            var rfcObj = requestFcPools.Checkout();
            rfcObj.order = OrderPool.Checkout();
            rfcObj.result.order = rfcObj.order;
            return rfcObj;
        }

        //private void MqInHandler(object sender, BasicDeliverEventArgs ea)
        //{
        //    Interlocked.Increment(ref mqInCnt);
        //    var bytes = ea.Body;
        //    RequestFromClient request;
        //    if(!requestFcPools.CheckoutLimited(out request))
        //    {
        //        RejectResponse(bytes);
        //        //mqRequest.MsgFinished(ea);
        //        Interlocked.Increment(ref mqRejCnt);
        //        return;
        //    }
        //    request.order = OrderPool.Checkout();
        //    request.result.order = request.order;
        //    request.FromBytes(bytes);
        //    RequestQueue.Enqueue(request);
        //    //mqRequest.MsgFinished(ea);
        //}

        //private void RejectResponse(byte[] bytes)
        //{
        //    //reject incoming orders, since we hit the limit
        //    var reject = ProcessOrderResult.ConstructRejectBuffer();
        //    mqOrderResponse.Enqueue(reject.bytes);
        //    ProcessOrderResult.CheckIn(reject);
        //    //Interlocked.Increment(ref Rejps);
        //}

        internal void HandleResponse()
        {
            //RequestFromClient request = null;
            while (!MatchingCoreSetup.Instance.cts.IsCancellationRequested)
            {
                try
                {
                    RequestFromClient request = null;
                    //Parallel.ForEach(ResponseQueue.GetConsumingEnumerable(MatchingCoreSetup.Instance.cts.Token), option, request =>
                    if (ResponseQueue.TryDequeue(out request))
                    {
                        //request = ResponseQueue.Take(MatchingCoreSetup.Instance.cts.Token);
                        #region send order response to order handling RabbitMQ
                        // Code here before result is being recycled
                        //mqOrderResponse.Enqueue(request.result);
                        RequestReceiver.Server.SendResponse(request.result);
                        #endregion

                        #region send transaction to transaction handling RabbitMQ
                        for (int i = 0; i < request.result.txList.Count; i++)
                        //Parallel.For(0, request.result.txList.Count, option, i =>
                        //Parallel.For(0, request.result.txList.Count, i =>
                        {
                            //mqTxResponse.Enqueue(request.result.txList[i]);
                            //Interlocked.Increment(ref Tps);
                            TxDistributor.Server.SendResponse(request.result.txList[i]);
                            TxPool.CheckIn(request.result.txList[i]);
                        }
                        //);
                        #endregion
                        if (request.result.CanRecycle)
                        {
                            OrderPool.Checkin(request.order);
                        }
                        request.result.txList.Clear();
                        request.result.ResetObj();
                        requestFcPools.Checkin(request);
                        //Interlocked.Increment(ref Rps);
                    }
                    //);
                }
                catch (OperationCanceledException)
                {
                    NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "HandleResponse Thread shutting down");
                }
                catch (Exception ex)
                {
                    NLogger.Instance.WriteLog(NLogger.LogLevel.Error, ex.ToString());
                }
                finally
                {
                }
            }
            NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "HandleResponse thread shutdown");
        }

        internal void HandleRequest()
        {
            while (!MatchingCoreSetup.Instance.cts.IsCancellationRequested)
            {
                try
                {
                    RequestFromClient request = null;
                    //var request = RequestQueue.Take(MatchingCoreSetup.Instance.cts.Token);
                    if(RequestQueue.TryDequeue(out request))
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
                        request.result.dt = DateTime.Now;
                        ResponseQueue.Enqueue(request);
                    }
                }
                catch (OperationCanceledException)
                {
                    NLogger.Instance.WriteLog(NLogger.LogLevel.Info, "HandleRequest Thread shutting down");
                }
                catch (Exception ex)
                {
                    NLogger.Instance.WriteLog(NLogger.LogLevel.Error, ex.ToString());
                }
            }
        }

        internal void callback()
        {
            //decimal matchingTotal = 0;
            //long opsTotal = 0;
            //long tpsTotal = 0;
            //long rejTotal = 0;
            while (!MatchingCoreSetup.Instance.cts.IsCancellationRequested)
            {
                Console.Clear();
                try
                {
                    //var tmp1 = Interlocked.Exchange(ref Rps, 0);
                    //var tmp2 = Interlocked.Exchange(ref Tps, 0);
                    //var tmp3 = Interlocked.Exchange(ref Rejps, 0);
                    //long tmp4 = Interlocked.Exchange(ref createTicks, 0);
                    //long tmp5 = Interlocked.Exchange(ref removeKeysTicks, 0);
                    //long tmp6 = Interlocked.Exchange(ref matchingTicks, 0);
                    //long tmp7 = Interlocked.Exchange(ref matchingBuyTicks, 0);
                    //long tmp8 = Interlocked.Exchange(ref matchingSellTicks, 0);
                    //Console.WriteLine(tmp1 + " reqest per sec");
                    //Console.WriteLine(tmp2 + " tx per sec");
                    //Console.WriteLine(tmp3 + " reject per sec");
                    //opsTotal += tmp1;
                    //tpsTotal += tmp2;
                    //rejTotal += tmp3;
                    //Console.WriteLine(opsTotal + " reqest total");
                    //Console.WriteLine(tpsTotal + " tx total");
                    //Console.WriteLine(rejTotal + " reject total");
                    Console.WriteLine(ProcessOrder.Instance.GetAskDepthLevel() + " sellKeys");
                    Console.WriteLine(ProcessOrder.Instance.GetBidDepthLevel() + " buyKeys");
                    //var tmpMatching = decimal.Round(new decimal(matchingTicks), 4);
                    //Console.WriteLine(decimal.Round(new decimal(createTicks), 4) + " ms - create order");
                    //Console.WriteLine(decimal.Round(new decimal(removeKeysTicks), 4) + " ms - removeKeys");
                    //Console.WriteLine(tmpMatching + " ms - matching");
                    //Console.WriteLine(decimal.Round(new decimal(matchingBuyTicks), 4) + " ms - matching buy");
                    //Console.WriteLine(decimal.Round(new decimal(matchingSellTicks), 4) + " ms - matching sell");
                    //Console.WriteLine(decimal.Round(new decimal(insertMemoryTicks), 4) + " insertMemory ms");
                    //Console.WriteLine(sortingTicks + " sorting ms");
                    //if (tmpMatching > 0) matchingTotal += tmpMatching;
                    //Console.WriteLine(matchingTotal + " matching total ms");
                    Console.WriteLine((ProcessOrder.Instance.lowestAsk == double.MaxValue ? 0 : ProcessOrder.Instance.lowestAsk) + " ask");
                    Console.WriteLine(ProcessOrder.Instance.highestBid + " bid");
                    int tmp1 = Interlocked.Exchange(ref mqInCnt, 0);
                    int tmp2 = Interlocked.Exchange(ref mqRejCnt, 0);
                    Console.WriteLine("mqInCnt:" + tmp1);
                    Console.WriteLine("mqRejCnt:" + tmp2);
                    Console.WriteLine("RequestQueue.bWritingQueueA:" + RequestQueue.bWritingQueueA);
                    Console.WriteLine("RequestQueue.QueueCount:" + RequestQueue.QueueCount);
                    Console.WriteLine("ResponseQueue.bWritingQueueA:" + ResponseQueue.bWritingQueueA);
                    Console.WriteLine("ResponseQueue.QueueCount:" + ResponseQueue.QueueCount);
                    var tmp3 = Interlocked.Exchange(ref Rps, 0);
                    Console.WriteLine("Rps:" + tmp3);
                    //ops = 0;
                    //createTicks = 0;
                    //removeKeysTicks = 0;
                    //matchingTicks = 0;
                    //matchingBuyTicks = 0;
                    //matchingSellTicks = 0;
                    //insertMemoryTicks = 0;
                    //sortingTicks = 0;
                    //List<double> asklist = new List<double>(ProcessOrder.Instance.GetLowest5Asks());
                    //List<double> bidlist = new List<double>(ProcessOrder.Instance.GetHighest5Bids());

                    //Console.WriteLine("==============================================");
                    //if (asklist.Count >= 5) Console.WriteLine(string.Format("{0:.00} ask5", asklist[4]));
                    //if (asklist.Count >= 4) Console.WriteLine(string.Format("{0:.00} ask4", asklist[3]));
                    //if (asklist.Count >= 3) Console.WriteLine(string.Format("{0:.00} ask3", asklist[2]));
                    //if (asklist.Count >= 2) Console.WriteLine(string.Format("{0:.00} ask2", asklist[1]));
                    //if (asklist.Count >= 1) Console.WriteLine(string.Format("{0:.00} ask1", asklist[0]));
                    //Console.WriteLine("==============================================");
                    //if (bidlist.Count >= 1) Console.WriteLine(string.Format("{0:.00} bid1", bidlist[0]));
                    //if (bidlist.Count >= 2) Console.WriteLine(string.Format("{0:.00} bid2", bidlist[1]));
                    //if (bidlist.Count >= 3) Console.WriteLine(string.Format("{0:.00} bid3", bidlist[2]));
                    //if (bidlist.Count >= 4) Console.WriteLine(string.Format("{0:.00} bid4", bidlist[3]));
                    //if (bidlist.Count >= 5) Console.WriteLine(string.Format("{0:.00} bid5", bidlist[4]));
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    Environment.Exit(ex.HResult);
                }
                Thread.Sleep(1000);
            }
        }
    }
}
