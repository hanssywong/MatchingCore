using MatchingLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MatchingCore
{
    /// <summary>
    /// The purpose of this class is no-lock, so it is single thread only among all the methods.
    /// So make sure you know what you are doing.
    /// Only one method can be called one at a time.
    /// </summary>
    internal class ProcessOrder
    {
        /// <summary>
        /// Singleton
        /// </summary>
        internal static ProcessOrder Instance { get; } = new ProcessOrder();
        /// <summary>
        /// Prices depth are included for matching
        /// </summary>
        List<double> PriceList { get; } = new List<double>(5000);
        /// <summary>
        /// Orders are ready to be remove. filled == volume
        /// </summary>
        List<int> removeOrders { get; } = new List<int>(5000);
        /// <summary>
        /// Bid Depth with individual price queue
        /// </summary>
        Dictionary<double, List<Order>> bidList { get; } = new Dictionary<double, List<Order>>(5000);
        /// <summary>
        /// Bid Depth
        /// </summary>
        List<double> bidL { get; } = new List<double>(5000);
        /// <summary>
        /// Ask Depth with individual price queue
        /// </summary>
        Dictionary<double, List<Order>> askList { get; } = new Dictionary<double, List<Order>>(5000);
        /// <summary>
        /// Ask Depth
        /// </summary>
        List<double> askL { get; } = new List<double>(5000);
        /// <summary>
        /// Order ID to Order Map
        /// </summary>
        Dictionary<string, Order> idToOrderMap { get; } = new Dictionary<string, Order>();
        internal double? highestBid { get; private set; } = null;
        internal double? lowestAsk { get; private set; } = null;

        internal void CheckAllOrNothing(RequestFromClient request)
        {
            Order order = request.order;
            MatchingOrderResult ret = request.result;
            Order aon = OrderPool.Checkout();
            aon.fv = order.fv;
            aon.p = order.p;
            aon.t = order.t;
            aon.v = order.v;
            if (aon.t == Order.OrderType.Buy && askList.Count > 0 && aon.p >= lowestAsk)
            {
                #region Matching Buy
                for (int i = 0; i < askL.Count; i++)
                {
                    if (aon.p >= askL[i])
                    {
                        PriceList.Add(askL[i]);
                    }
                    else break;
                }
                if (PriceList.Count > 0)
                {
                    for (int p = 0; p < PriceList.Count; p++)
                    {
                        var buySideVol = aon.v - aon.fv;
                        if (buySideVol == 0)
                            break;

                        for (int i = 0; i < askList[PriceList[p]].Count; i++)
                        {
                            #region Check if there is enough volume
                            var sellSideVol = askList[PriceList[p]][i].v - askList[PriceList[p]][i].fv;
                            buySideVol = aon.v - aon.fv;
                            if (buySideVol == 0)
                                break;

                            if (sellSideVol > buySideVol)
                            {
                                aon.fv = aon.v;
                            }
                            else if (sellSideVol == buySideVol)
                            {
                                aon.fv = aon.v;
                            }
                            else//sellSideVol < buySideVol
                            {
                                aon.fv += sellSideVol;
                            }
                            #endregion
                        }
                    }
                }
                #endregion
            }
            else if (aon.t == Order.OrderType.Sell && bidList.Count > 0 && aon.p <= highestBid)
            {
                #region Matching Sell
                for (int i = bidL.Count - 1; i >= 0; i--)
                {
                    if (aon.p <= bidL[i])
                    {
                        PriceList.Add(bidL[i]);
                    }
                    else break;
                }
                if (PriceList.Count > 0)
                {
                    for (int p = 0; p < PriceList.Count; p++)
                    {
                        var sellSideVol = aon.v - aon.fv;
                        if (sellSideVol == 0)
                            break;

                        for (int i = 0; i < bidList[PriceList[p]].Count; i++)
                        {
                            #region Check if there is enough volume
                            var buySideVol = bidList[PriceList[p]][i].v - bidList[PriceList[p]][i].fv;
                            sellSideVol = aon.v - aon.fv;
                            if (sellSideVol == 0)
                                break;

                            if (buySideVol > sellSideVol)
                            {
                                aon.fv = aon.v;
                            }
                            else if (sellSideVol == buySideVol)
                            {
                                aon.fv = aon.v;
                            }
                            else//buySideVol < sellSideVol
                            {
                                aon.fv += buySideVol;
                            }
                            #endregion
                        }
                    }
                }
                #endregion
            }
            if (aon.v == aon.fv)
            {
                ret.Success = true;
            }
            else
            {
                ret.Success = false;
            }
            OrderPool.Checkin(aon);

            if (PriceList.Count > 0) PriceList.Clear();
        }

        internal void IsOrderExist(RequestFromClient request)
        {
            MatchingOrderResult ret = request.result;
            if (!idToOrderMap.ContainsKey(request.order.id))
            {
                ret.Success = false;
                ret.Comment = "Order is missing in dict";
                return;
            }

            Order tmpOrder = idToOrderMap[request.order.id];
            if (tmpOrder.t == Order.OrderType.Buy)
            {
                if (bidList[tmpOrder.p].IndexOf(idToOrderMap[request.order.id]) == -1)
                {
                    ret.Success = false;
                    ret.Comment = "Order is missing in bid list";
                    return;
                }
            }
            else
            {
                if (bidList[tmpOrder.p].IndexOf(idToOrderMap[request.order.id]) == -1)
                {
                    ret.Success = false;
                    ret.Comment = "Order is missing in ask list";
                    return;
                }
            }

            ret.Success = true;
            ret.errorType = ProcessOrderResult.ErrorType.OrderExists;
        }

        internal void DoCancel(RequestFromClient request)
        {
            Order order = request.order;
            MatchingOrderResult ret = request.result;

            IsOrderExist(request);
            if(!request.result.Success)
            {
                request.result.Success = false;
                request.result.errorType = ProcessOrderResult.ErrorType.OrderIsMissing;
                return;
            }

            idToOrderMap.Remove(order.id);
            ret.Success = true;
            ret.errorType = ProcessOrderResult.ErrorType.Cancelled;
        }

        private void RecalculateLowestAskBid()
        {
            if (askL.Count > 0)
            {
                //askL.Sort();
                lowestAsk = askL[0];
            }
            else
                lowestAsk = double.MaxValue;
        }

        private void RecalculateHighestBid()
        {
            if (bidL.Count > 0)
            {
                highestBid = bidL[bidL.Count - 1];
            }
            else
                highestBid = 0;
        }

        internal void DoMatching(RequestFromClient request)
        {
            Order order = request.order;
            MatchingOrderResult ret = request.result;
            List<TxOutput> txList = request.result.txList;

            if (order.t == Order.OrderType.Buy && askList.Count > 0 && order.p >= lowestAsk)
            {
                #region Matching Buy
                for (int i = 0; i < askL.Count; i++)
                {
                    if (order.p >= askL[i])
                    {
                        PriceList.Add(askL[i]);
                    }
                    else break;
                }
                if (PriceList.Count > 0)
                {
                    for (int p = 0; p < PriceList.Count; p++)
                    {
                        var buySideVol = order.v - order.fv;
                        if (buySideVol == 0)
                            break;

                        for (int i = 0; i < askList[PriceList[p]].Count; i++)
                        {
                            #region Execute
                            var sellSideVol = askList[PriceList[p]][i].v - askList[PriceList[p]][i].fv;
                            buySideVol = order.v - order.fv;
                            if (buySideVol == 0)
                                break;

                            if (sellSideVol > buySideVol)
                            {
                                var sell = askList[PriceList[p]][i];
                                sell.fv += buySideVol;
                                order.fv = order.v;

                                TxOutput tran = TxPool.Checkout();
                                tran.bt = order.id;
                                tran.p = askList[PriceList[p]][i].p;
                                tran.st = askList[PriceList[p]][i].id;
                                tran.v = buySideVol;
                                tran.dt = DateTime.Now;
                                tran.init = Transaction.InitiatorType.Buy;
                                txList.Add(tran);
                            }
                            else if (sellSideVol == buySideVol)
                            {
                                #region Remove Order
                                Order removeOrder = askList[PriceList[p]][i];
                                removeOrders.Add(i);
                                idToOrderMap.Remove(removeOrder.id);
                                #endregion

                                removeOrder.fv = removeOrder.v;
                                order.fv = order.v;

                                TxOutput tran = TxPool.Checkout();
                                tran.bt = order.id;
                                tran.p = removeOrder.p;
                                tran.st = removeOrder.id;
                                tran.v = buySideVol;
                                tran.dt = DateTime.Now;
                                tran.init = Transaction.InitiatorType.Buy;
                                txList.Add(tran);
                                OrderPool.Checkin(removeOrder);
                            }
                            else//sellSideVol < buySideVol
                            {
                                #region Remove Order
                                Order removeOrder = askList[PriceList[p]][i];
                                removeOrders.Add(i);
                                idToOrderMap.Remove(removeOrder.id);
                                #endregion

                                removeOrder.fv = removeOrder.v;
                                order.fv += sellSideVol;

                                TxOutput tran = TxPool.Checkout();
                                tran.bt = order.id;
                                tran.p = removeOrder.p;
                                tran.st = removeOrder.id;
                                tran.v = sellSideVol;
                                tran.dt = DateTime.Now;
                                tran.init = Transaction.InitiatorType.Buy;
                                txList.Add(tran);
                                OrderPool.Checkin(removeOrder);
                            }
                            #endregion
                        }

                        if (askList[PriceList[p]].Count == 0 || askList[PriceList[p]].Count == removeOrders.Count)
                        {
                            askList.Remove(PriceList[p]);
                            askL.Remove(PriceList[p]);
                            if (PriceList[p] == lowestAsk)
                            {
                                RecalculateLowestAskBid();
                            }
                        }
                        else if (removeOrders.Count > 0)
                        {
                            askList[PriceList[p]].RemoveRange(0, removeOrders.Count);
                        }
                        removeOrders.Clear();
                    }
                }
                #endregion
            }
            else if (order.t == Order.OrderType.Sell && bidList.Count > 0 && order.p <= highestBid)
            {
                #region Matching Sell
                for (int i = bidL.Count - 1; i >= 0; i--)
                {
                    if (order.p <= bidL[i])
                    {
                        PriceList.Add(bidL[i]);
                    }
                    else break;
                }
                if (PriceList.Count > 0)
                {
                    for (int p = 0; p < PriceList.Count; p++)
                    {
                        var sellSideVol = order.v - order.fv;
                        if (sellSideVol == 0)
                            break;

                        for (int i = 0; i < bidList[PriceList[p]].Count; i++)
                        {
                            #region Execute
                            var buySideVol = bidList[PriceList[p]][i].v - bidList[PriceList[p]][i].fv;
                            sellSideVol = order.v - order.fv;
                            if (sellSideVol == 0)
                                break;

                            if (buySideVol > sellSideVol)
                            {
                                var buy = bidList[PriceList[p]][i];
                                buy.fv += sellSideVol;
                                order.fv = order.v;

                                TxOutput tran = TxPool.Checkout();
                                tran.st = order.id;
                                tran.p = bidList[PriceList[p]][i].p;
                                tran.bt = bidList[PriceList[p]][i].id;
                                tran.v = sellSideVol;
                                tran.dt = DateTime.Now;
                                tran.init = Transaction.InitiatorType.Sell;
                                txList.Add(tran);
                            }
                            else if (sellSideVol == buySideVol)
                            {
                                #region Remove Order
                                Order removeOrder = bidList[PriceList[p]][i];
                                removeOrders.Add(i);
                                idToOrderMap.Remove(removeOrder.id);
                                #endregion

                                removeOrder.fv = removeOrder.v;
                                order.fv = order.v;

                                TxOutput tran = TxPool.Checkout();
                                tran.st = order.id;
                                tran.p = removeOrder.p;
                                tran.bt = removeOrder.id;
                                tran.v = sellSideVol;
                                tran.dt = DateTime.Now;
                                tran.init = Transaction.InitiatorType.Sell;
                                txList.Add(tran);
                                OrderPool.Checkin(removeOrder);
                            }
                            else//buySideVol < sellSideVol
                            {
                                #region Remove Order
                                Order removeOrder = bidList[PriceList[p]][i];
                                removeOrders.Add(i);
                                idToOrderMap.Remove(removeOrder.id);
                                #endregion

                                removeOrder.fv = removeOrder.v;
                                order.fv += buySideVol;

                                TxOutput tran = TxPool.Checkout();
                                tran.st = order.id;
                                tran.p = removeOrder.p;
                                tran.bt = removeOrder.id;
                                tran.v = buySideVol;
                                tran.dt = DateTime.Now;
                                tran.init = Transaction.InitiatorType.Sell;
                                txList.Add(tran);
                                OrderPool.Checkin(removeOrder);
                            }
                            #endregion
                        }

                        if (bidList[PriceList[p]].Count == 0 || bidList[PriceList[p]].Count == removeOrders.Count)
                        {
                            bidList.Remove(PriceList[p]);
                            bidL.Remove(PriceList[p]);
                            if (PriceList[p] == highestBid)
                            {
                                RecalculateHighestBid();
                            }
                        }
                        else if (removeOrders.Count > 0)
                        {
                            bidList[PriceList[p]].RemoveRange(0, removeOrders.Count);
                        }
                        removeOrders.Clear();
                    }
                }
                #endregion
            }

            if (order.v > order.fv)
            {
                if (order.et != Order.ExecutionType.IoC && order.et != Order.ExecutionType.AoN)
                {
                    #region insert into memory queue
                    if (order.t == 0)
                    {
                        if (highestBid == null || order.p > highestBid) highestBid = order.p;
                        if (!bidList.ContainsKey(order.p))
                        {
                            bidList.Add(order.p, new List<Order>());
                            bidL.Add(order.p);
                            bidL.Sort();
                        }
                        bidList[order.p].Add(order);
                    }
                    else
                    {
                        if (lowestAsk == null || order.p < lowestAsk) lowestAsk = order.p;
                        if (!askList.ContainsKey(order.p))
                        {
                            askList.Add(order.p, new List<Order>());
                            askL.Add(order.p);
                            askL.Sort();
                        }
                        askList[order.p].Add(order);
                    }
                    #endregion
                    ret.CanRecycle = false;
                }
            }

            if (PriceList.Count > 0) PriceList.Clear();
            ret.Success = true;
            ret.errorType = ProcessOrderResult.ErrorType.Success;
        }

        internal int GetBidDepthLevel()
        {
            return this.bidL.Count;
        }
        internal int GetAskDepthLevel()
        {
            return this.askL.Count;
        }

        internal IList<double> GetHighest5Bids()
        {
            List<double> list = null;
            if (bidL.Count >= 5)
            {
                list = new List<double>(bidL.GetRange(bidL.Count - 6, 5));
            }
            else if (bidL.Count == 0) return new List<double>();
            else
            {
                list = new List<double>(bidL);
            }
            list.Reverse();
            return list.ToArray();
        }

        internal IList<double> GetLowest5Asks()
        {
            List<double> list = null;
            if (askL.Count >= 5)
            {
                list = new List<double>(askL.GetRange(0, 5));
            }
            else if (askL.Count == 0) return new List<double>();
            else
            {
                list = new List<double>(askL);
            }
            return list.ToArray();
        }
    }
}
