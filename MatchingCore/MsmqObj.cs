using LogHelper;
using MatchingLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Messaging;
using System.Text;
using System.Threading.Tasks;

namespace MatchingCore
{
    internal class OutMsmqObj
    {
        public MessageQueue txMsmq { get; }
        public OutMsmqObj(string mqname)
        {
            txMsmq = new MessageQueue(mqname);
            txMsmq.Formatter = new XmlMessageFormatter();
        }

        public void ThrowIn(List<Transaction> txs)
        {

        }
    }
    internal class InMsmqObj
    {
        public MessageQueue txMsmq { get; }

        public InMsmqObj(string mqname)
        {
            txMsmq = new MessageQueue(mqname);
            txMsmq.Formatter = new XmlMessageFormatter();
            txMsmq.ReceiveCompleted += new ReceiveCompletedEventHandler(RequestReceived);
        }

        /// <summary>
        /// Move to ProcessRequest class
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        [Obsolete]
        private void RequestReceived(object sender, ReceiveCompletedEventArgs e)
        {
            if (!MatchingCoreSetup.Instance.cts.IsCancellationRequested)
            {
                //once a message is received, stop receiving
                using (var msMessage = txMsmq.EndReceive(e.AsyncResult))
                {
                    try
                    {
                        msMessage.Formatter = new XmlMessageFormatter(new Type[] { typeof(string) });
                        //do something with the message
                        string json = msMessage.Body as string;
                        //RequestFromClient request;
                        //if (!requestFcPools.CheckoutLimited(out request))
                        //{
                        //    //reject incoming orders, since we hit the limit
                        //}
                        //request.order = ProcessOrder.Instance.OrderPoolCheckOut();
                        //JsonConvert.PopulateObject(json, request);
                        //ProcessRequest.Instance.RequestQueue.Add(request);
                    }
                    catch (Exception ex)
                    {
                        LibraryLogger.Instance.WriteLog(LibraryLogger.libLogLevel.Error, ex.ToString());
                    }
                }

                //begin receiving again
                // changes to get request after previous finish
                //q.BeginReceive();
            }
        }
    }
}
