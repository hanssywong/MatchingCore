﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MatchingLib;

namespace MatchingCore
{
    internal class RequestFromClient : RequestToMatching
    {
        internal MatchingOrderResult result { get; set; } = new MatchingOrderResult() { txList = new List<TxOutput>(10000) };
        public new void FromBytes(byte[] bytes)
        {
            base.FromBytes(bytes);
            result.order = order;
        }
    }
}
